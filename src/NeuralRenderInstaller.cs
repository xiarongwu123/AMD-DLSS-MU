using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mu.Neural;

namespace AmdNrAssistant;

public sealed record NeuralInstalledFile(string Name, string Sha256, string Source);
public sealed record NeuralInstallManifest(string Scheme, string Backend, int Passes, string Proxy,
    string PackageVersion, string GraphicsApi, string Verification, string RuleRevision,
    List<NeuralInstalledFile> InstalledFiles, List<ManagedIniKey> ModifiedConfigKeys, string BackupId,
    NeuralOptions Options);

public static class NeuralRenderInstaller
{
    public const int Mode = 6; // 0 remains the legacy Daniel installation record.
    public const string Version = "OptScaler-NR 1.12.1";
    public const string MochizukiModelHash = "2b41c888cf4155b8958c665ba64018ab0bd25c85fc71a2b6db86d0d04d1f7fbd";
    public const string NvidiaSourceHash = "e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e";
    const string PythonUrl = "https://www.python.org/ftp/python/3.13.9/python-3.13.9-embed-amd64.zip";
    const long PythonSize = 10926675;
    const string PythonHash = "91d828c2da3a029b41699e918674a0cb379c02cf20dab9c501306885f837402a";
    const string PythonExeHash = "08a64dc73ac3e3776b49f0097c6306bdb9c8f7990a037065213324d328467bf5";
    const string DanielRuntimeHash = "195c4a891b6eac4c1cb7671e10ff62bbbe2b17f1dfae1344dc5a6714e4775721";
    public sealed record FilePin(long Size, string Sha256);
    public sealed record PackagePin(string Version, string Url, long Size, string Sha256, Dictionary<string, FilePin> Files);
    sealed record SourceFile(string Name, string Path, string Hash, string Origin);
    public static PackagePin Pin()
    {
        using var stream = typeof(NeuralRenderInstaller).Assembly.GetManifestResourceStream("Mu.NeuralRendering.Pins")
            ?? throw new IOException("神经渲染资源清单缺失。");
        return JsonSerializer.Deserialize<PackagePin>(stream, NeuralRules.Json) ?? throw new IOException("资源清单无效。");
    }
    internal static string? ResourcesOverride { get; set; }
    public static string Resources => ResourcesOverride ?? Path.Combine(Core.InstallerCacheDirectory, "neural-user-resources");
    static bool Safe(string name) => !Path.IsPathRooted(name) && !name.Contains(':') &&
        name.Replace('\\', '/').Split('/').All(p => p.Length > 0 && p != "." && p != "..");
    public static bool ManagedPath(string name)
    {
        name = name.Replace('\\', '/');
        return Safe(name) && (name is "LmxxfNrRuntime.dll" or "LmxxfNrRuntime.pak" or "MochizukiNrRuntime.dll" ||
            new[] { "dlssnr-amd/", "lmxxf-modules/", "shaders/", "native-game-tiled-assets/", "experimental_lighting/" }
                .Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
    static bool PackagePath(string name) => name is "OptiScaler.dll" or "OptiScaler.ini" or "LICENSE" or "NOTICE" ||
        new[] { "OptiScaler/", "Licenses/", "dlssnr-amd/", "lmxxf-modules/", "shaders/", "experimental_lighting/", "model-tools/" }
            .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) ||
        name is "LmxxfNrRuntime.dll" or "MochizukiNrRuntime.dll" or "mochizuki-model.py";
    static bool BackendPath(string name, string backend) => name.StartsWith("OptiScaler/", StringComparison.Ordinal) ||
        name.StartsWith("Licenses/", StringComparison.Ordinal) || name is "OptiScaler.dll" or "OptiScaler.ini" or "LICENSE" or "NOTICE" ||
        backend == "mochizuki" && (name == "MochizukiNrRuntime.dll" || name.StartsWith("dlssnr-amd/", StringComparison.Ordinal)) ||
        backend == "lmxxf" && (name == "LmxxfNrRuntime.dll" || new[] { "lmxxf-modules/", "shaders/", "experimental_lighting/" }.Any(p => name.StartsWith(p, StringComparison.Ordinal)));

    static PackagePin PinFor(string backend) => backend == NeuralBackends.LmxxfRdna3 ? AmdNrRdna3Package.Pin() : Pin();
    static bool SelectedPackagePath(string name, string backend) => backend == NeuralBackends.LmxxfRdna3 ? AmdNrRdna3Package.PackagePath(name) : PackagePath(name);
    public static void ExtractVerified(string archiveFile, string destination, string backend = "")
    {
        var pin = PinFor(backend); Core.RejectLinks(archiveFile); Core.RejectLinks(destination);
        if (new FileInfo(archiveFile).Length != pin.Size || Core.Hash(archiveFile) != pin.Sha256)
            throw new IOException("神经渲染发布包 SHA256 校验失败。");
        using var archive = ZipFile.OpenRead(archiveFile);
        if (archive.Entries.Count > 1500) throw new IOException("发布包文件过多。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            if (!Safe(name) || !seen.Add(name) || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
                throw new IOException("发布包含非法路径或重复文件。");
            if (!SelectedPackagePath(name, backend)) continue; // no launchers, scripts or optional upscalers
            var maximum = backend == NeuralBackends.LmxxfRdna3 && name == "LmxxfNrRuntime.pak" ? 1024L * 1024 * 1024 : 256L * 1024 * 1024;
            if (!pin.Files.TryGetValue(name, out var expected) || entry.Length != expected.Size || entry.Length > maximum)
                throw new IOException("发布包组件不匹配：" + name);
            var target = Path.Combine(destination, name); Core.RejectLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew)) input.CopyTo(output);
            if (Core.Hash(target) != expected.Sha256) throw new IOException("解压组件校验失败：" + name);
        }
        ValidatePackage(destination, backend);
    }
    public static void ValidatePackage(string folder, string backend = "")
    {
        Core.RejectLinks(folder);
        foreach (var pair in PinFor(backend).Files.Where(p => SelectedPackagePath(p.Key, backend)))
        {
            var path = Path.Combine(folder, pair.Key); Core.RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != pair.Value.Size || Core.Hash(path) != pair.Value.Sha256)
                throw new IOException("资源包缺少或损坏组件：" + pair.Key);
        }
        foreach (var name in backend == NeuralBackends.LmxxfRdna3 ? new[] { "OptiScaler.dll", "LmxxfNrRuntime.dll" } : new[] { "OptiScaler.dll", "MochizukiNrRuntime.dll", "LmxxfNrRuntime.dll" })
            Core.CheckPe(Path.Combine(folder, name), true);
    }
    public static async Task<string> PrepareAsync(IProgress<string> progress, CancellationToken token, string backend = "")
    {
        var pin = PinFor(backend); var cache = Path.Combine(Core.InstallerCacheDirectory, (backend == NeuralBackends.LmxxfRdna3 ? "AMDNR-RDNA3-" : "OptScaler-NR-") + pin.Version);
        Core.RejectLinks(cache); Directory.CreateDirectory(cache);
        var zip = Path.Combine(cache, "package.zip"); Core.RejectLinks(zip);
        if (!File.Exists(zip) || new FileInfo(zip).Length != pin.Size || Core.Hash(zip) != pin.Sha256)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(http, pin.Url,
                backend == NeuralBackends.LmxxfRdna3 ? null : "https://api.github.com/repos/TheAutomatic/dlss-5-amd-project/releases/assets/643822225",
                pin.Sha256, pin.Size, zip,
                new Progress<int>(n => progress.Report($"神经渲染资源下载 {n}%")), token, progress.Report);
        }
        token.ThrowIfCancellationRequested(); progress.Report("正在校验神经渲染文件…");
        var stage = Path.Combine(cache, "extract-" + Guid.NewGuid().ToString("N"));
        try { await Task.Run(() => ExtractVerified(zip, stage, backend), token); return stage; }
        catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
    }
    static IEnumerable<string> Files(string root)
    {
        Core.RejectLinks(root);
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root)) { Core.RejectLinks(file); yield return file; }
        foreach (var dir in Directory.EnumerateDirectories(root)) foreach (var file in Files(dir)) yield return file;
    }
    static string? Find(string resources, string gameRoot, params string[] names)
    {
        foreach (var root in new[] { resources, gameRoot }) foreach (var name in names)
        { var path = Path.Combine(root, name); Core.RejectLinks(path); if (File.Exists(path)) return path; }
        return null;
    }
    public static string DanielImageHash(string file)
    {
        Core.RejectLinks(file); Core.CheckPe(file, true);
        if (new FileInfo(file).Length > 128L * 1024 * 1024) throw new IOException("Daniel Runtime 文件过大。");
        using var input = File.OpenRead(file); using var pe = new PEReader(input);
        var end = pe.PEHeaders.SectionHeaders.Max(s => (long)s.PointerToRawData + s.SizeOfRawData);
        if (end <= 0 || end > input.Length) throw new IOException("Daniel Runtime PE 节无效。");
        input.Position = 0; var bytes = new byte[checked((int)end)]; input.ReadExactly(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    static List<SourceFile> UserFiles(string resources, string gameRoot, string backend)
    {
        var result = new List<SourceFile>();
        void Add(string name, string path) { Core.RejectLinks(path); result.Add(new(name, path, Core.Hash(path), "user-supplied")); }
        if (backend == "mochizuki")
        {
            var model = Find(resources, gameRoot, "dlssnr-amd/dlssnr.bin", "dlssnr.bin")
                ?? throw new IOException("模型尚未准备好，请重试一键配置。MU 会从内置资源自动提取并校验。");
            if (new FileInfo(model).Length != 147756560 || Core.Hash(model) != MochizukiModelHash)
                throw new IOException("Mochizuki 模型不匹配：需要 310.8.0 对应的已校验模型，游戏文件尚未修改。");
            Add("dlssnr-amd/dlssnr.bin", model);
        }
        else if (backend == "daniel")
        {
            var runtime = Find(resources, gameRoot, "dlssnr_amd_pass1.dll", "version.dll")
                ?? throw new IOException("Daniel 缺少用户 Runtime。请从作者官方 v0.6.0 准备 version.dll 和权重，再通过“准备本机资源”导入；MU 不会启动第三方 Setup。");
            if (DanielImageHash(runtime) != DanielRuntimeHash) throw new IOException("Daniel Runtime 不是上游白名单中的 0.6.0 文件。");
            for (var i = 1; i <= 3; i++) Add($"dlssnr_amd_pass{i}.dll", runtime);
            var weights = Find(resources, gameRoot, "dlssnr_on_amd_weights.bin") ?? throw new IOException("Daniel 缺少 dlssnr_on_amd_weights.bin，请导入你有权使用的权重。");
            if (new FileInfo(weights).Length is < 1048576 or > 536870912) throw new IOException("Daniel 权重大小异常或不完整。");
            Add("dlssnr_on_amd_weights.bin", weights);
            var nv = Find(resources, gameRoot, "nvngx_dlssnr.dll") ?? throw new IOException("内置 DLSS 5 文件尚未准备好，请重新运行一键配置。");
            Core.ValidateDll(nv);
            if (Core.Hash(nv) != NvidiaSourceHash && Core.Hash(nv) != Core.BundledDllSha256) throw new IOException("NVIDIA DLL 不在已核验的 310.8.0 文件列表中。");
            Add("nvngx_dlssnr.dll", nv);
        }
        else
        {
            var root = new[] { Path.Combine(resources, "native-game-tiled-assets"), Path.Combine(gameRoot, "native-game-tiled-assets") }
                .FirstOrDefault(p => File.Exists(Path.Combine(p, "block0-ffn.f16")) || File.Exists(Path.Combine(p, "block0-ffn.f32")))
                ?? throw new IOException("lmxxf 缺少 native-game-tiled-assets，请提供合法持有并获准使用的完整模型目录。");
            var files = Files(root).ToArray();
            if (files.Length is < 1 or > 2000 || files.Sum(f => new FileInfo(f).Length) > 4L * 1024 * 1024 * 1024)
                throw new IOException("lmxxf 模型目录数量或大小异常。");
            foreach (var file in files)
            {
                if (new FileInfo(file).Length == 0 || !new[] { ".bin", ".f16", ".f32", ".i32", ".json" }.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    throw new IOException("lmxxf 模型目录包含空文件或非模型文件。");
                Add("native-game-tiled-assets/" + Path.GetRelativePath(root, file).Replace('\\', '/'), file);
            }
        }
        return result;
    }
    public static string[] MissingResources(string exe, NeuralPlan plan)
    {
        // The matched AMDNR pak is acquired and verified with its host at install
        // time. Never ask the player for the incompatible RDNA4 loose assets.
        if (plan.Backend == NeuralBackends.LmxxfRdna3) return [];
        if (plan.Backend == "mochizuki") return MissingMochizukiResources(exe, CanPrepareMochizukiModel);
        try { UserFiles(Resources, Path.GetDirectoryName(exe)!, plan.Backend); return []; }
        catch (IOException error) { return [error.Message]; }
    }
    internal static string[] MissingMochizukiResources(string exe, bool canPrepare)
    {
        // Invalid or incomplete cached/user models are recoverable using the pinned
        // bundled source. Preflight must allow the installer to prepare a replacement.
        if (HasVerifiedMochizukiModel(exe) || canPrepare) return [];
        return ["Mochizuki 模型缺失或校验失败，且 MU 内置 DLSS 5 资源不可用。请重新安装完整版本的 MU，或导入配套的 310.8.0 模型。"];
    }
    public static bool HasVerifiedMochizukiModel(string exe)
    {
        var model = Find(Resources, Path.GetDirectoryName(exe)!, "dlssnr-amd/dlssnr.bin", "dlssnr.bin");
        return model != null && new FileInfo(model).Length == 147756560 && Core.Hash(model) == MochizukiModelHash;
    }
    public static bool CanPrepareMochizukiModel =>
        typeof(NeuralRenderInstaller).Assembly.GetManifestResourceInfo("AmdNrAssistant.nvngx_dlssnr.dll") != null;
    public static string[] ResourcePreparation(string exe, NeuralPlan plan)
    {
        if (MissingResources(exe, plan).Length > 0) return [];
        if (plan.Backend == NeuralBackends.LmxxfRdna3)
            return ["首次配置需下载约 1 GB 的 AMDNR 配套运行库与内核包；MU 会校验文件并复用已下载的缓存。"];
        if (plan.Backend == "mochizuki" && !HasVerifiedMochizukiModel(exe) && CanPrepareMochizukiModel)
            return ["Mochizuki 模型尚未就绪。点击一键配置后，MU 会下载配套组件，从内置 310.8.0 资源提取 dlssnr.bin 并校验；首次需要联网并等待数分钟。"];
        return [];
    }
    public static void EnsureBundledDll()
    {
        var file = Path.Combine(Resources, "nvngx_dlssnr.dll"); Core.RejectLinks(file);
        if (File.Exists(file) && Core.Hash(file) is NvidiaSourceHash or Core.BundledDllSha256) return;
        if (File.Exists(file)) throw new IOException("已有 nvngx_dlssnr.dll 与 MU 内置版本不一致；请移走缓存中的冲突文件后重试。");
        Core.ExtractBundledDll(file);
    }
    // Imports only data and the explicitly reviewed runtime. Never imports setup
    // executables, scripts, unrelated DLLs, or symlinks from a user's folder.
    public static void ImportResources(string folder)
    {
        Core.RejectLinks(folder); Core.RejectLinks(Resources); Directory.CreateDirectory(Resources);
        var files = Files(folder).Take(2001).ToArray();
        if (files.Length > 2000) throw new IOException("请选择专用资源目录，文件过多。");
        long total = 0;
        foreach (var file in files)
        {
            var name = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (!Safe(name)) throw new IOException("资源路径无效。");
            if (name is not ("dlssnr-amd/dlssnr.bin" or "dlssnr.bin" or "nvngx_dlssnr.dll" or "version.dll" or "dlssnr_amd_pass1.dll" or "dlssnr_on_amd_weights.bin") &&
                !(name.StartsWith("native-game-tiled-assets/", StringComparison.Ordinal) && new[] { ".f16", ".f32", ".i32", ".bin", ".json" }.Contains(Path.GetExtension(name)))) continue;
            total += new FileInfo(file).Length; if (total > 4L * 1024 * 1024 * 1024) throw new IOException("导入资源过大。");
            var target = Path.Combine(Resources, name); Core.RejectLinks(target);
            if (Path.GetFullPath(file) == Path.GetFullPath(target)) continue;
            AtomicCopy(file, target, Core.Hash(file));
        }
    }
    public static async Task PrepareModelAsync(string package, IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var existing = Find(Resources, Resources, "dlssnr-amd/dlssnr.bin", "dlssnr.bin");
        if (existing != null && new FileInfo(existing).Length == 147756560 && Core.Hash(existing) == MochizukiModelHash) return;
        if (existing != null) progress.Report("已有模型校验未通过，正在自动重新提取；原缓存将在新模型校验成功后备份。 ");
        await Task.Run(() => ValidatePackage(package), token);
        progress.Report("正在从 MU 内置资源准备神经渲染模型，首次运行可能需要几分钟…");
        // Extract into a private stage, never truncate a cached or game-owned model.
        var modelStage = Path.Combine(Resources, "model-stage-" + Guid.NewGuid().ToString("N"));
        Core.RejectLinks(modelStage); Directory.CreateDirectory(modelStage);
        var output = Path.Combine(modelStage, "dlssnr.bin");
        var nv = Path.Combine(package, "mu-model-source", "nvngx_dlssnr.dll");
        var script = Path.Combine(package, "mu-mochizuki-model.py");
        try
        {
            await Task.Run(() => Core.ExtractBundledDll(nv), token);
            // The pinned upstream script accepts one 310.8.0 file hash. MU's bundled
            // 310.8.0 DLL has different metadata but produces the same verified model.
            var source = await File.ReadAllTextAsync(Path.Combine(package, "mochizuki-model.py"), token);
            if (source.Split(NvidiaSourceHash).Length != 2) throw new IOException("上游模型提取脚本已变化，已停止以免生成错误模型。");
            await File.WriteAllTextAsync(script, source.Replace(NvidiaSourceHash, Core.BundledDllSha256, StringComparison.Ordinal), token);
            var python = await PreparePythonAsync(progress, token);
            var info = new System.Diagnostics.ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = package };
            if (Path.GetFileName(python).Equals("py.exe", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add("-3");
            foreach (var arg in new[] { "-X", "utf8", script, nv, output, "--work", Path.Combine(package, "model-work") }) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info) ?? throw new IOException("无法启动 Python。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(true); } catch { } throw; }
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length != 147756560 || Core.Hash(output) != MochizukiModelHash)
                throw new IOException("模型自动提取或校验失败，游戏文件尚未修改。" + (await stderr).Trim().Split('\n').LastOrDefault());
            token.ThrowIfCancellationRequested();
            PromoteMochizukiModel(output);
            progress.Report("Mochizuki 模型已自动准备并通过校验。");
        }
        finally
        {
            if (File.Exists(script)) File.Delete(script);
            if (File.Exists(nv)) File.Delete(nv);
            if (Directory.Exists(modelStage)) Directory.Delete(modelStage, true);
        }
    }
    internal static void PromoteMochizukiModel(string output)
    {
        Core.RejectLinks(output);
        if (!File.Exists(output) || new FileInfo(output).Length != 147756560 || Core.Hash(output) != MochizukiModelHash)
            throw new IOException("新模型校验失败，保留现有缓存与游戏文件。");
        var destination = Path.Combine(Resources, "dlssnr-amd/dlssnr.bin"); Core.RejectLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var backup = destination + ".bak-" + Guid.NewGuid().ToString("N");
            Core.RejectLinks(backup); File.Copy(destination, backup, false);
        }
        AtomicCopy(output, destination, MochizukiModelHash);
    }
    static async Task<string> PreparePythonAsync(IProgress<string> progress, CancellationToken token)
    {
        var cache = Path.Combine(Core.InstallerCacheDirectory, "python-3.13.9-embed-amd64");
        Core.RejectLinks(cache); Directory.CreateDirectory(cache);
        var zip = Path.Combine(cache, "python.zip"); Core.RejectLinks(zip);
        if (!File.Exists(zip) || new FileInfo(zip).Length != PythonSize || Core.Hash(zip) != PythonHash)
        {
            progress.Report("正在下载模型提取组件…");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(http, PythonUrl, null, PythonHash, PythonSize, zip,
                new Progress<int>(n => progress.Report($"模型提取组件下载 {n}%")), token, progress.Report);
        }
        var runtime = Path.Combine(cache, "runtime");
        var exe = Path.Combine(runtime, "python.exe"); Core.RejectLinks(exe);
        // Check every executable/runtime byte before running the cached interpreter.
        if (File.Exists(exe) && Core.Hash(exe) == PythonExeHash && RuntimeMatchesArchive(zip, runtime)) return exe;
        progress.Report("正在准备模型提取组件…");
        var stage = Path.Combine(cache, "runtime-" + Guid.NewGuid().ToString("N")); Core.RejectLinks(stage);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count > 100) throw new IOException("模型提取组件文件数异常。");
            foreach (var entry in archive.Entries)
            {
                if (!Safe(entry.FullName) || entry.FullName.Contains('/') || entry.FullName.Contains('\\') ||
                    (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000 || entry.Length > 30L * 1024 * 1024)
                    throw new IOException("模型提取组件包含非法文件。");
                var target = Path.Combine(stage, entry.FullName); Core.RejectLinks(target);
                Directory.CreateDirectory(stage);
                using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew);
                await input.CopyToAsync(output, token);
            }
            if (Core.Hash(Path.Combine(stage, "python.exe")) != PythonExeHash) throw new IOException("模型提取组件校验失败。");
            if (Directory.Exists(runtime)) Directory.Delete(runtime, true);
            Directory.Move(stage, runtime);
            return exe;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    static bool RuntimeMatchesArchive(string zip, string runtime)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            if (!Safe(entry.FullName) || entry.FullName.Contains('/') || entry.FullName.Contains('\\')) return false;
            var file = Path.Combine(runtime, entry.FullName); Core.RejectLinks(file);
            if (!File.Exists(file) || new FileInfo(file).Length != entry.Length) return false;
            using var stream = entry.Open();
            var expected = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (Core.Hash(file) != expected) return false;
        }
        return true;
    }
    internal static string BytesHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void AtomicWrite(string target, byte[] bytes)
    {
        Core.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".mu-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void AtomicCopy(string source, string target, string expected, bool overwrite = true)
    {
        Core.RejectLinks(source); Core.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".mu-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temp, false);
            if (Core.Hash(temp) != expected) throw new IOException("组件复制校验失败：" + Path.GetFileName(target));
            File.Move(temp, target, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Install(string exe, string package, NeuralPlan plan, CancellationToken token = default, IProgress<string>? progress = null)
    {
        Core.ValidateGame(exe); Core.CheckPe(exe, false); GameManagement.EnsureClosed(exe); NeuralRenderPlanner.Validate(plan.Options);
        if (!NeuralRules.Backends.Contains(plan.Backend) || !NeuralRules.Proxies.Contains(plan.Proxy) ||
            !NeuralRules.Apis.Contains(plan.GraphicsApi) || plan.Passes < 1 || plan.Passes > (plan.Backend == "mochizuki" ? 30 : 3))
            throw new IOException("安装计划无效。");
        if (GameManagement.Read(exe) is { Phase: not "restored" }) throw new IOException("请先按安装记录恢复旧方案，再切换或升级。");
        ValidatePackage(package, plan.Backend); var root = Path.GetDirectoryName(exe)!;
        if (root.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("不能向 WindowsApps 写入插件，请选择可写的游戏目录。");
        var conflicting = GameManagement.Loaders.Where(n => File.Exists(Path.Combine(root, n))).ToArray();
        if (conflicting.Length > 0) throw new IOException("已有注入代理或 OptiScaler / ReShade 冲突：" + string.Join("、", conflicting) + "。请先恢复或卸载对应插件。");
        var rdna3 = plan.Backend == NeuralBackends.LmxxfRdna3;
        var pin = PinFor(plan.Backend); var files = pin.Files.Where(p => (rdna3 ? AmdNrRdna3Package.PackagePath(p.Key) : BackendPath(p.Key, plan.Backend)) && p.Key != "OptiScaler.ini")
            .Select(p => new SourceFile(rdna3 ? AmdNrRdna3Package.InstalledName(p.Key, plan.Proxy) : p.Key == "OptiScaler.dll" ? plan.Proxy : p.Key is "LICENSE" or "NOTICE" ? "Licenses/OptScaler-NR-" + p.Key + ".txt" : p.Key,
                Path.Combine(package, p.Key), p.Value.Sha256, "official-release:" + pin.Version)).ToList();
        if (!rdna3) files.AddRange(UserFiles(Resources, root, plan.Backend));
        if (files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new IOException("安装清单包含重复文件。");
        var expectedBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var target = Path.Combine(root, f.Name); Core.RejectLinks(target);
            if (!GameManagement.IsTracked(f.Name) || Directory.Exists(target)) throw new IOException("无法跟踪目标文件：" + f.Name);
            // Preserve existing exact dependencies/models. Never overwrite foreign files.
            var before = File.Exists(target) ? Core.Hash(target) : "";
            if (before.Length > 0 && before != f.Hash) throw new IOException("组件冲突，未修改游戏：" + f.Name);
            expectedBefore[f.Name] = before;
        }
        var iniPath = Path.Combine(root, "OptiScaler.ini"); Core.RejectLinks(iniPath);
        var iniExisted = File.Exists(iniPath);
        var original = iniExisted ? File.ReadAllBytes(iniPath) : File.ReadAllBytes(Path.Combine(package, "OptiScaler.ini"));
        expectedBefore["OptiScaler.ini"] = iniExisted ? BytesHash(original) : "";
        var (text, encoding) = ManagedIni.Decode(original); var changes = new List<ManagedIniKey>();
        var libraryPath = ManagedIni.Get(text, "Libraries", "OptiDllPath");
        if (libraryPath != null && !new[] { "auto", "OptiScaler" }.Contains(libraryPath, StringComparer.OrdinalIgnoreCase))
            throw new IOException("现有 OptiScaler.ini 使用自定义依赖路径；请先在高级配置中核对路径。MU 不会覆盖这项设置。");
        if (rdna3 && ManagedIni.Get(text, "DlssNr", "AmdRuntimePath") is { Length: > 0 })
            throw new IOException("现有 AMDNR 配置使用外部 Runtime 路径，请先核对 AmdRuntimePath；MU 不会将其覆盖为本机包。");
        foreach (var pair in NeuralRenderPlanner.Settings(plan))
        {
            changes.Add(new("DlssNr", pair.Key, ManagedIni.Get(text, "DlssNr", pair.Key), pair.Value));
            text = ManagedIni.Set(text, "DlssNr", pair.Key, pair.Value);
        }
        changes.Add(new("ProcessFilter", "TargetProcessName", ManagedIni.Get(text, "ProcessFilter", "TargetProcessName"), Path.GetFileName(exe)));
        text = ManagedIni.Set(text, "ProcessFilter", "TargetProcessName", Path.GetFileName(exe));
        var updated = ManagedIni.Encode(text, encoding);
        var manifest = new NeuralInstallManifest("dlss5-neural-rendering", plan.Backend, plan.Passes, plan.Proxy, pin.Version,
            plan.GraphicsApi, plan.Status, plan.RuleRevision, files.Select(f => new NeuralInstalledFile(f.Name, f.Hash, f.Origin))
                .Append(new NeuralInstalledFile("OptiScaler.ini", BytesHash(updated), "mu-managed-keys")).ToList(),
            changes, Guid.NewGuid().ToString("N"), plan.Options);
        var planned = files.ToDictionary(f => f.Name, f => f.Hash, StringComparer.OrdinalIgnoreCase); planned.Add("OptiScaler.ini", BytesHash(updated));
        token.ThrowIfCancellationRequested(); Begin(exe, manifest, planned, expectedBefore);
        try
        {
            var index = 0;
            foreach (var f in files)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(root, f.Name); Core.RejectLinks(target);
                if (expectedBefore[f.Name] == f.Hash)
                {
                    if (!File.Exists(target) || Core.Hash(target) != f.Hash) throw new IOException("已复用组件发生并发变更：" + f.Name);
                }
                else AtomicCopy(f.Path, target, f.Hash, false);
                if (index++ % 20 == 0) progress?.Report("正在部署组件：" + f.Name);
            }
            token.ThrowIfCancellationRequested();
            if ((File.Exists(iniPath) ? Core.Hash(iniPath) : "") != expectedBefore["OptiScaler.ini"])
                throw new IOException("OptiScaler.ini 在安装期间被其他程序修改，已停止并回滚。");
            AtomicWrite(iniPath, updated);
            foreach (var f in planned)
                if (Core.Hash(Path.Combine(root, f.Key)) != f.Value) throw new IOException("安装后校验失败：" + f.Key);
            var record = GameManagement.Read(exe)!; GameManagement.Save(record with { Phase = "installed" });
        }
        catch (Exception error)
        {
            try { Restore(exe, GameManagement.Read(exe)!, false); }
            catch (Exception rollback) { throw new IOException("安装失败且回滚未完成；已保留安装记录和备份，请使用恢复配置。", new AggregateException(error, rollback)); }
            throw;
        }
    }
    static void Begin(string exe, NeuralInstallManifest manifest, Dictionary<string, string> planned, Dictionary<string, string> expectedBefore)
    {
        var root = Path.GetDirectoryName(exe)!; var files = new List<ManagedFile>();
        foreach (var pair in planned)
        {
            var path = Path.Combine(root, pair.Key); Core.RejectLinks(path);
            var before = File.Exists(path) ? Core.Hash(path) : "";
            if (before != expectedBefore[pair.Key]) throw new IOException("备份前组件已发生并发变更：" + pair.Key);
            if (before.Length > 0 && before != pair.Value) AtomicCopy(path, Backup(exe, manifest.BackupId, pair.Key), before);
            files.Add(new(pair.Key, before, pair.Value));
        }
        // Intent is durable before the first game write, so a crash is recoverable.
        GameManagement.Save(new GameRecord(exe, Mode, manifest.Backend == NeuralBackends.LmxxfRdna3 ? "AMDNR " + manifest.PackageVersion : Version, DateTime.UtcNow, "installing", files) { Neural = manifest });
    }
    static string Backup(string exe, string id, string name)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !GameManagement.IsTracked(name)) throw new IOException("安装清单备份路径无效。");
        var path = Path.Combine(GameManagement.State(exe), "backups", id, name); Core.RejectLinks(path); return path;
    }
    public static void Restore(string exe, GameRecord record, bool preserveChangedSettings)
    {
        GameManagement.EnsureClosed(exe); var manifest = record.Neural ?? throw new IOException("缺少神经渲染 manifest。");
        if (record.Mode != Mode || manifest.Scheme != "dlss5-neural-rendering" || record.Files.Any(f => !GameManagement.IsTracked(f.Name)))
            throw new IOException("神经渲染安装清单包含无效方案或路径。");
        var root = Path.GetDirectoryName(exe)!; var writes = new Dictionary<string, byte[]>(); var deletes = new List<string>();
        var dirs = record.Files.Where(f => f.Before.Length == 0 && f.Name.Replace('\\', '/').Contains('/'))
            .Select(f => f.Name.Replace('\\', '/').Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(name => GameManagement.ComponentDirectories(Path.Combine(root, name))).OrderByDescending(p => p.Length).ToArray();
        foreach (var f in record.Files.Where(f => f.Before != f.After))
        {
            if (!GameManagement.IsTracked(f.Name)) throw new IOException("安装记录含非法文件路径。");
            var path = Path.Combine(root, f.Name); Core.RejectLinks(path); var current = File.Exists(path) ? Core.Hash(path) : "";
            if (f.Before.Length > 0)
            {
                var backup = Backup(exe, manifest.BackupId, f.Name);
                if (!File.Exists(backup) || Core.Hash(backup) != f.Before) throw new IOException("安装前备份损坏：" + f.Name);
            }
            if (current == f.Before) continue;
            if (f.Name == "OptiScaler.ini" && current.Length > 0 && current != f.After)
            {
                var bytes = File.ReadAllBytes(path); var (text, encoding) = ManagedIni.Decode(bytes);
                foreach (var key in manifest.ModifiedConfigKeys)
                {
                    var value = ManagedIni.Get(text, key.Section, key.Key);
                    if (value != key.After && value != key.Before && !preserveChangedSettings)
                        throw new IOException("MU 管理的配置已被修改：" + key.Key + "，请在恢复时保留修改备份。");
                    text = ManagedIni.Set(text, key.Section, key.Key, key.Before);
                }
                // Keep unrelated changes made since installation, even for a newly created INI.
                writes[path] = ManagedIni.Encode(text, encoding); continue;
            }
            if (current != f.After && current.Length != 0) throw new IOException("组件已被其他程序修改，未执行恢复：" + f.Name);
            if (f.Before.Length > 0) writes[path] = File.ReadAllBytes(Backup(exe, manifest.BackupId, f.Name));
            else if (current.Length > 0) deletes.Add(path);
        }
        // All preflight checks completed before mutation. Archive edited INI first.
        var iniPath = Path.Combine(root, "OptiScaler.ini");
        if (writes.ContainsKey(iniPath) && File.Exists(iniPath))
            AtomicCopy(iniPath, Path.Combine(GameManagement.State(exe), "settings-" + Guid.NewGuid().ToString("N"), "OptiScaler.ini"), Core.Hash(iniPath));
        foreach (var pair in writes) AtomicWrite(pair.Key, pair.Value);
        foreach (var file in deletes) { Core.RejectLinks(file); File.Delete(file); }
        foreach (var dir in dirs) if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        GameManagement.Save(record with { Phase = "restored" });
    }
    public static void UpdateSettings(string exe, NeuralPlan plan)
    {
        GameManagement.EnsureClosed(exe); var record = GameManagement.Read(exe);
        if (record is not { Phase: "installed", Neural: not null } || record.Neural.Backend != plan.Backend || record.Neural.Proxy != plan.Proxy || record.Neural.PackageVersion != plan.PackageVersion)
            throw new IOException("切换后端或代理需要先恢复，再按新方案部署。");
        var path = Path.Combine(Path.GetDirectoryName(exe)!, "OptiScaler.ini"); Core.RejectLinks(path);
        var before = File.ReadAllBytes(path); var (text, encoding) = ManagedIni.Decode(before);
        var keys = new List<ManagedIniKey>(record.Neural.ModifiedConfigKeys);
        foreach (var pair in NeuralRenderPlanner.Settings(plan))
        {
            if (!keys.Any(k => k.Section == "DlssNr" && k.Key == pair.Key)) keys.Add(new("DlssNr", pair.Key, ManagedIni.Get(text, "DlssNr", pair.Key), pair.Value));
            else keys = keys.Select(k => k.Section == "DlssNr" && k.Key == pair.Key ? k with { After = pair.Value } : k).ToList();
            text = ManagedIni.Set(text, "DlssNr", pair.Key, pair.Value);
        }
        var after = ManagedIni.Encode(text, encoding);
        AtomicCopy(path, Path.Combine(GameManagement.State(exe), "settings-" + Guid.NewGuid().ToString("N"), "OptiScaler.ini"), BytesHash(before));
        var next = record with { Neural = record.Neural with { Passes = plan.Passes, Verification = plan.Status,
            RuleRevision = plan.RuleRevision, Options = plan.Options, ModifiedConfigKeys = keys,
            InstalledFiles = record.Neural.InstalledFiles.Select(f => f.Name == "OptiScaler.ini" ? f with { Sha256 = BytesHash(after) } : f).ToList() },
            Files = record.Files.Select(f => f.Name == "OptiScaler.ini" ? f with { After = BytesHash(after) } : f).ToList() };
        // Write intent first; if the atomic file replacement fails, keep the old record.
        GameManagement.Save(next);
        try
        {
            if (Core.Hash(path) != BytesHash(before)) throw new IOException("配置被其他程序修改，未保存高级设置。");
            AtomicWrite(path, after);
        }
        catch { GameManagement.Save(record); throw; }
    }
}
