using System.IO.Compression;
using System.Net.Http;
using SharpCompress.Archives.SevenZip;

namespace AmdNrAssistant;

// DLSS-NR and XeFG are installed as one tracked transaction. The two reviewed
// release archives are fetched separately; third-party setup EXEs are never run.
public static class CombinedRenderInstaller
{
    public const string BaseUrl = "https://github.com/TheAutomatic/dlss-5-amd-project/releases/download/v1.10.3.1/OptScaler-NR-1.10.3.1.zip";
    public const string BaseDigest = "2604511bc069dc0eabdebc1b7351b80522a22a625ea49fd8aaa5e80d652d91c4";
    public const long BaseSize = 149435227;
    public const string OverlayUrl = "https://github.com/a756598009-cmyk/DLSS5-6x-AMD-OptiScaler/releases/download/v10.6/DLSS5-6x-AMD-OptiScaler-v10.6.7z";
    public const string OverlayDigest = "abaecd8451a94b4e82f49906ebe840a76c926806a2c2bd28343a47412fb8c9a5";
    public const long OverlaySize = 334015796;

    static bool Safe(string name) => !Path.IsPathRooted(name) && !name.Contains(':') &&
        name.Replace('\\', '/').Split('/').All(p => p.Length > 0 && p != "." && p != "..");

    static bool PackageFile(string name) => name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("D3D12_Optiscaler/D3D12Core.dll", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("OptiScaler/", StringComparison.OrdinalIgnoreCase) &&
        new[] { ".dll", ".ini", ".asi" }.Contains(Path.GetExtension(name).ToLowerInvariant());

    static void CopyVerified(Stream input, string target, long expected)
    {
        Core.RejectLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = new FileStream(target, FileMode.CreateNew);
        var buffer = new byte[81920]; long size = 0; int read;
        while ((read = input.Read(buffer)) > 0)
        {
            size += read;
            if (size > expected) throw new IOException("组件长度异常。");
            output.Write(buffer, 0, read);
        }
        if (size != expected) throw new IOException("组件解压不完整。");
    }

    static void ExtractBase(string archivePath, string target)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 512) throw new IOException("基础包文件数量异常。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!Safe(name) || !PackageFile(name) || entry.Length == 0) continue;
            if (!seen.Add(name) || entry.Length > 256L * 1024 * 1024) throw new IOException("基础包存在重复或超大组件。");
            using var input = entry.Open();
            CopyVerified(input, Path.Combine(target, name.Replace('/', Path.DirectorySeparatorChar)), entry.Length);
        }
        foreach (var required in new[] { "OptiScaler.dll", "OptiScaler.ini", "OptiScaler/libxess_fg.dll" })
            if (!seen.Contains(required)) throw new IOException("基础包缺少组件：" + required);
    }

    static void ExtractOverlay(string archivePath, string target)
    {
        using var archive = SevenZipArchive.OpenArchive(archivePath);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToArray();
        if (entries.Length > 512) throw new IOException("扩展包文件数量异常。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = (entry.Key ?? "").Replace('\\', '/');
            if (!Safe(name) || entry.LinkTarget != null) throw new IOException("扩展包路径无效。");
            if (!PackageFile(name) && name is not ("version.dll" or "dlssnr_on_amd_weights.bin")) continue;
            if (!seen.Add(name) || entry.Size <= 0 || entry.Size > 256L * 1024 * 1024)
                throw new IOException("扩展包存在重复或无效组件。");
            var path = Path.Combine(target, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) File.Delete(path); // reviewed overlay supersedes the base dependency
            using var input = entry.OpenEntryStream(); CopyVerified(input, path, entry.Size);
        }
        foreach (var required in new[] { "version.dll", "dlssnr_on_amd_weights.bin", "OptiScaler/libxess_fg.dll" })
            if (!seen.Contains(required)) throw new IOException("扩展包缺少组件：" + required);
    }

    public static void ExtractVerified(string baseFile, string overlayFile, string target)
    {
        Core.RejectLinks(baseFile); Core.RejectLinks(overlayFile); Core.RejectLinks(target);
        if (new FileInfo(baseFile).Length != BaseSize || Core.Hash(baseFile) != BaseDigest ||
            new FileInfo(overlayFile).Length != OverlaySize || Core.Hash(overlayFile) != OverlayDigest)
            throw new IOException("组合方案发布包校验失败。");
        ExtractBase(baseFile, target);
        ExtractOverlay(overlayFile, target);
    }

    public static string Configure(string template, string exe)
    {
        var ini = OptiInstaller.SetIni(template, "ProcessFilter", "TargetProcessName", Path.GetFileName(exe));
        foreach (var (section, key, value) in new[] {
            ("Menu", "OverlayMenu", "true"), ("DlssNr", "Enabled", "true"),
            ("DlssNr", "NrBackend", "daniel"), ("DlssNr", "Quality", "1"),
            ("FrameGen", "External", "false"), ("FrameGen", "Enabled", "true"),
            ("FrameGen", "FGInput", "upscaler"), ("FrameGen", "FGOutput", "xefg"),
            ("XeFG", "InterpolationCount", "1") })
            ini = OptiInstaller.SetIni(ini, section, key, value);
        return ini;
    }

    public static async Task<string> PrepareAsync(string exe, IProgress<string> progress, CancellationToken token)
    {
        var cache = Path.Combine(Core.InstallerCacheDirectory, "DLSS5-XeFG-v10.6");
        Core.RejectLinks(cache); Directory.CreateDirectory(cache);
        var baseFile = Path.Combine(cache, "base.zip");
        var overlayFile = Path.Combine(cache, "overlay.7z");
        async Task Fetch(string path, string url, string api, string digest, long size, string label)
        {
            if (File.Exists(path) && new FileInfo(path).Length == size && Core.Hash(path) == digest) return;
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(client, url, api, digest, size, path,
                new Progress<int>(n => progress.Report(label + " " + n + "%")), token,
                progress.Report);
        }
        await Fetch(baseFile, BaseUrl,
            "https://api.github.com/repos/TheAutomatic/dlss-5-amd-project/releases/assets/615224514",
            BaseDigest, BaseSize, "神经渲染组件");
        await Fetch(overlayFile, OverlayUrl,
            "https://api.github.com/repos/a756598009-cmyk/DLSS5-6x-AMD-OptiScaler/releases/assets/618107255",
            OverlayDigest, OverlaySize, "多帧生成组件");
        token.ThrowIfCancellationRequested();
        var target = Path.Combine(cache, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => ExtractVerified(baseFile, overlayFile, target), token);
            foreach (var name in new[] { "OptiScaler.dll", "version.dll", "OptiScaler/libxess_fg.dll" })
                Core.CheckPe(Path.Combine(target, name.Replace('/', Path.DirectorySeparatorChar)), true);
            return target;
        }
        catch { if (Directory.Exists(target)) Directory.Delete(target, true); throw; }
    }

    public static void Install(string exe, string package)
    {
        Core.ValidateGame(exe); GameManagement.EnsureClosed(exe);
        var root = Path.GetDirectoryName(exe)!;
        var sources = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .Select(p => (Source: p, Name: Path.GetRelativePath(package, p).Replace('\\', '/')))
            .Where(f => PackageFile(f.Name)).ToList();
        sources.RemoveAll(f => f.Name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase));
        sources.Add((Path.Combine(package, "OptiScaler.dll"), "dxgi.dll"));
        sources.Add((Path.Combine(package, "OptiScaler.ini"), "OptiScaler.ini"));
        for (var i = 1; i <= 3; i++) sources.Add((Path.Combine(package, "version.dll"), $"dlssnr_amd_pass{i}.dll"));
        sources.Add((Path.Combine(package, "dlssnr_on_amd_weights.bin"), "dlssnr_on_amd_weights.bin"));
        if (Directory.Exists(Path.Combine(root, "OptiScaler"))) throw new IOException("游戏已有 OptiScaler 文件夹，请先恢复或卸载旧方案。");
        foreach (var (_, name) in sources)
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)); Core.RejectLinks(path);
            if (!GameManagement.IsTracked(name) || File.Exists(path) || Directory.Exists(path))
                throw new IOException("目标目录存在冲突或无法跟踪：" + name);
        }
        var ini = Configure(File.ReadAllText(Path.Combine(package, "OptiScaler.ini")), exe);
        GameManagement.Begin(exe, 4, "DLSS5 + XeFG 2X (v1.10.3.1/v10.6)");
        try
        {
            foreach (var (source, name) in sources)
            {
                var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)); Core.RejectLinks(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (name == "OptiScaler.ini") File.WriteAllText(path, ini);
                else { File.Copy(source, path, false); if (Core.Hash(source) != Core.Hash(path)) throw new IOException("组件复制校验失败：" + name); }
            }
            GameManagement.Finish(exe, true);
        }
        catch { GameManagement.Finish(exe, false); GameManagement.Restore(exe); throw; }
    }
}
