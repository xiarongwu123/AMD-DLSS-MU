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
        return Safe(name) && (name is "LmxxfNrRuntime.dll" or "MochizukiNrRuntime.dll" ||
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

    public static void ExtractVerified(string archiveFile, string destination)
    {
        var pin = Pin(); Core.RejectLinks(archiveFile); Core.RejectLinks(destination);
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
            if (!PackagePath(name)) continue; // no Setup/Uninstall scripts or optional person worker/models
            if (!pin.Files.TryGetValue(name, out var expected) || entry.Length != expected.Size || entry.Length > 256L * 1024 * 1024)
                throw new IOException("发布包组件不匹配：" + name);
            var target = Path.Combine(destination, name); Core.RejectLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew)) input.CopyTo(output);
            if (Core.Hash(target) != expected.Sha256) throw new IOException("解压组件校验失败：" + name);
        }
        ValidatePackage(destination);
    }
    public static void ValidatePackage(string folder)
    {
        Core.RejectLinks(folder);
        foreach (var pair in Pin().Files.Where(p => PackagePath(p.Key)))
        {
            var path = Path.Combine(folder, pair.Key); Core.RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != pair.Value.Size || Core.Hash(path) != pair.Value.Sha256)
                throw new IOException("资源包缺少或损坏组件：" + pair.Key);
        }
        foreach (var name in new[] { "OptiScaler.dll", "MochizukiNrRuntime.dll", "LmxxfNrRuntime.dll" })
            Core.CheckPe(Path.Combine(folder, name), true);
    }
    public static async Task<string> PrepareAsync(IProgress<string> progress, CancellationToken token)
    {
        var pin = Pin(); var cache = Path.Combine(Core.InstallerCacheDirectory, "OptScaler-NR-" + pin.Version);
        Core.RejectLinks(cache); Directory.CreateDirectory(cache);
        var zip = Path.Combine(cache, "package.zip"); Core.RejectLinks(zip);
        if (!File.Exists(zip) || new FileInfo(zip).Length != pin.Size || Core.Hash(zip) != pin.Sha256)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(http, pin.Url, null, pin.Sha256, pin.Size, zip,
                new Progress<int>(n => progress.Report($"神经渲染资源下载 {n}%")), token, progress.Report);
        }
        token.ThrowIfCancellationRequested(); progress.Report("正在校验神经渲染文件…");
        var stage = Path.Combine(cache, "extract-" + Guid.NewGuid().ToString("N"));
        try { await Task.Run(() => ExtractVerified(zip, stage), token); return stage; }
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
                ?? throw new IOException("Mochizuki 缺少用户模型。请提供合法持有的 dlssnr.bin，或导入 nvngx_dlssnr.dll 310.8.0 后由 MU 提取。");
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
            var nv = Find(resources, gameRoot, "nvngx_dlssnr.dll") ?? throw new IOException("Daniel 缺少 nvngx_dlssnr.dll 310.8.0，请自行提供有权使用的文件。");
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
        try { UserFiles(Resources, Path.GetDirectoryName(exe)!, plan.Backend); return []; }
        catch (IOException error) { return [error.Message]; }
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
        if (Find(Resources, Resources, "dlssnr-amd/dlssnr.bin", "dlssnr.bin") != null) return;
        var nv = Path.Combine(Resources, "nvngx_dlssnr.dll"); Core.RejectLinks(nv);
        if (!File.Exists(nv)) return;
        if (Core.Hash(nv) != NvidiaSourceHash) throw new IOException("Mochizuki 提取需要上游指定 SHA256 的 nvngx_dlssnr.dll 310.8.0；其他版本不能替代。");
        await Task.Run(() => ValidatePackage(package), token);
        progress.Report("正在从用户提供的 DLL 提取 Mochizuki 模型（仅读取数据）…");
        var output = Path.Combine(Resources, "dlssnr-amd/dlssnr.bin"); Core.RejectLinks(output);
        var info = new System.Diagnostics.ProcessStartInfo("py.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = package };
        foreach (var arg in new[] { "-3", "-X", "utf8", Path.Combine(package, "mochizuki-model.py"), nv, output, "--work", Path.Combine(package, "model-work") }) info.ArgumentList.Add(arg);
        try
        {
            using var process = System.Diagnostics.Process.Start(info) ?? throw new IOException("无法启动 Python。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(true); } catch { } throw; }
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(output) || Core.Hash(output) != MochizukiModelHash)
                throw new IOException("模型提取失败。请安装 Python 3.10+，或自行提供已校验的 dlssnr.bin。游戏文件尚未修改。");
        }
        catch (System.ComponentModel.Win32Exception e) { throw new IOException("未找到 Python 启动器。请安装 Python 3.10+（包含 py 启动器），或导入已提取的 dlssnr.bin。", e); }
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
        ValidatePackage(package); var root = Path.GetDirectoryName(exe)!;
        if (root.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("不能向 WindowsApps 写入插件，请选择可写的游戏目录。");
        var conflicting = GameManagement.Loaders.Where(n => File.Exists(Path.Combine(root, n))).ToArray();
        if (conflicting.Length > 0) throw new IOException("已有注入代理或 OptiScaler / ReShade 冲突：" + string.Join("、", conflicting) + "。请先恢复或卸载对应插件。");
        var pin = Pin(); var files = pin.Files.Where(p => BackendPath(p.Key, plan.Backend) && p.Key != "OptiScaler.ini")
            .Select(p => new SourceFile(p.Key == "OptiScaler.dll" ? plan.Proxy : p.Key is "LICENSE" or "NOTICE" ? "Licenses/OptScaler-NR-" + p.Key + ".txt" : p.Key,
                Path.Combine(package, p.Key), p.Value.Sha256, "official-release:" + pin.Version)).ToList();
        files.AddRange(UserFiles(Resources, root, plan.Backend));
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
            if (before.Length > 0) AtomicCopy(path, Backup(exe, manifest.BackupId, pair.Key), before);
            files.Add(new(pair.Key, before, pair.Value));
        }
        // Intent is durable before the first game write, so a crash is recoverable.
        GameManagement.Save(new GameRecord(exe, Mode, Version, DateTime.UtcNow, "installing", files) { Neural = manifest });
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
        foreach (var f in record.Files)
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
        if (record is not { Phase: "installed", Neural: not null } || record.Neural.Backend != plan.Backend || record.Neural.Proxy != plan.Proxy)
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
