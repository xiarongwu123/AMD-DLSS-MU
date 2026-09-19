using System.Diagnostics;
using System.Reflection;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public record ReleaseInfo(string Tag, string Url, string Sha256, long Size);
public record Change(string Name, bool Existed, string OriginalHash, string NewHash, bool Changed);
public record Journal(string Target, string Release, string Phase, List<Change> Changes);
public record InstallCheck(bool Ready, string Message, string? ProxyName, bool HasConfig, bool HasWeights);
public record AlternativeChange(string RelativePath, bool Existed, string OriginalHash, string NewHash);
public record AlternativeJournal(string Target, List<AlternativeChange> Changes);

public static class Core
{
    public const string Repository = "https://github.com/danielblnc/DLSS-NR-on-AMD";
    public const string InstallerName = "dlssnr_on_amd_setup.exe";
    public const string DllName = "nvngx_dlssnr.dll";
    public const string ReviewedTag = "v0.3.1";
    public const string RequiredDll = "310.8.0.0";
    public const string BundledDllResource = "nvngx_dlssnr.dll";
    // Fixed asset shipped in the Windows EXE. This lets us distinguish a
    // missing/quarantined asset from an upstream installer failure.
    public const string BundledDllSha256 = "8270b350cd82de5ce89806872cdd6b6a9249b80836b91bbeb3573470744cc206";
    // Pin both the online release and cached installer to the reviewed bytes.
    public const string ReviewedInstallerSha256 = "cf7ada1486b499700a84846b342ca2b1defdb4db622843f812151f255f2ad63c";
    public const long ReviewedInstallerSize = 7598347;
    // Names accepted by the upstream installer as the game's proxy DLL.
    // The installer chooses one of these; the presence of nvngx_dlssnr.dll alone
    // does not mean that the proxy was installed or that the game will load it.
    public static readonly string[] ProxyNames = ["version.dll", "winmm.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll", "dxgi.dll"];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Hash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    public static void RejectLinks(string path)
    {
        var full = Path.GetFullPath(path);
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不支持符号链接或目录联接，请选择真实游戏目录：" + current);
        }
    }

    public static void CheckPe(string path, bool dll)
    {
        using var f = File.OpenRead(path);
        using var r = new BinaryReader(f);
        if (f.Length < 96 || r.ReadUInt16() != 0x5a4d) throw new IOException("文件不是有效的 Windows 程序：" + path);
        f.Position = 0x3c;
        var offset = r.ReadInt32();
        if (offset < 64 || offset > f.Length - 24) throw new IOException("PE 文件头无效。");
        f.Position = offset;
        if (r.ReadUInt32() != 0x4550 || r.ReadUInt16() != 0x8664) throw new IOException("需要 x64 游戏程序或 x64 DLL。");
        f.Position = offset + 22;
        if (((r.ReadUInt16() & 0x2000) != 0) != dll) throw new IOException("文件类型与所选 EXE / DLL 不符。");
    }

    public static string ExtractBundledDll(string destination)
    {
        RejectLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.nvngx_dlssnr.dll")
            ?? throw new IOException("内置 DLSS 5 DLL 资源缺失。");
        var temp = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                source.CopyTo(target);
            // Validate using the final canonical filename. The temporary filename has a
            // suffix and must never be passed through the exact-name guard.
            File.Move(temp, destination, true);
            try { ValidateDll(destination); }
            catch { try { File.Delete(destination); } catch { } throw; }
            var hash = Hash(destination);
            if (!string.Equals(hash, BundledDllSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(destination); } catch { }
                throw new IOException("内置 DLSS DLL 哈希不匹配，可能被安全软件修改或构建资源错误。实际：" + hash);
            }
            return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void ValidateGame(string exe)
    {
        if (!string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(exe), InstallerName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择游戏本体 EXE，不要选择安装器。");
        RejectLinks(exe);
        CheckPe(exe, false);
        if (Path.GetDirectoryName(Path.GetFullPath(exe)) == Path.GetPathRoot(Path.GetFullPath(exe)))
            throw new IOException("请使用独立游戏文件夹，不能直接安装到磁盘根目录。");
    }

    public static void ValidateDll(string path)
    {
        if (!string.Equals(Path.GetFileName(path), DllName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择 nvngx_dlssnr.dll，其他 DLSS DLL 不能代替。");
        RejectLinks(path);
        CheckPe(path, true);
        var v = FileVersionInfo.GetVersionInfo(path);
        if (v.FileMajorPart != 310 || v.FileMinorPart != 8 || v.FileBuildPart != 0 || v.FilePrivatePart != 0)
            throw new IOException("需要 DLL 版本 310.8.0.0；当前文件版本为 " + v.FileVersion);
    }

    public static void ValidateInstaller(string path)
    {
        if (!string.Equals(Path.GetFileName(path), InstallerName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装器文件名不正确：" + InstallerName);
        RejectLinks(path);
        CheckPe(path, false);
    }

    public static InstallCheck CheckInstalled(string directory)
    {
        directory = Path.GetFullPath(directory);
        var dll = Path.Combine(directory, DllName);
        var config = Path.Combine(directory, "dlssnr_on_amd.ini");
        var weights = Path.Combine(directory, "dlssnr_on_amd_weights.bin");
        var proxy = FindProxy(directory);
        var hasConfig = HasContent(config);
        var hasWeights = HasContent(weights);
        if (!File.Exists(dll))
            return new InstallCheck(false, "安装器完成后未找到 nvngx_dlssnr.dll；请确认它与游戏 EXE 在同一目录。", proxy, hasConfig, hasWeights);
        try { ValidateDll(dll); }
        catch (Exception error)
        {
            return new InstallCheck(false, "游戏目录中的 nvngx_dlssnr.dll 不是已核验的 310.8.0.0 版本：" + error.Message, proxy, hasConfig, hasWeights);
        }
        if (!hasConfig || !hasWeights)
            return new InstallCheck(false, "官方安装器没有生成完整配置（缺少 dlssnr_on_amd.ini 或 dlssnr_on_amd_weights.bin）。请在安装器窗口中完成安装，并确认 DLL 版本为 310.8.0.0。", proxy, hasConfig, hasWeights);
        if (proxy == null)
            return new InstallCheck(false, "未找到官方安装器生成的代理 DLL。游戏必须加载 version.dll、winmm.dll、dbghelp.dll、wininet.dll、winhttp.dll 或 dxgi.dll 之一。", null, hasConfig, hasWeights);
        return new InstallCheck(true, "安装器已生成代理 DLL、配置和权重文件。启动游戏后启用 FSR，再按 End 打开菜单。", proxy, hasConfig, hasWeights);
    }

    static bool HasContent(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    static string? FindProxy(string directory)
    {
        foreach (var name in ProxyNames)
        {
            var path = Path.Combine(directory, name);
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    CheckPe(path, true);
                    return name;
                }
            }
            catch { }
        }
        return null;
    }

    public static string InstallerCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "downloads");

    public static string? FindCachedInstaller()
    {
        var candidates = new[]
        {
            Path.Combine(InstallerCacheDirectory, ReviewedTag, InstallerName),
            Path.Combine(AppContext.BaseDirectory, InstallerName)
        };
        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                ValidateInstaller(path);
                if (new FileInfo(path).Length != ReviewedInstallerSize) continue;
                if (!string.Equals(Hash(path), ReviewedInstallerSha256, StringComparison.OrdinalIgnoreCase)) continue;
                return path;
            }
            catch { }
        }
        return null;
    }

    public static void CacheInstaller(string installer)
    {
        ValidateInstaller(installer);
        if (new FileInfo(installer).Length != ReviewedInstallerSize ||
            !string.Equals(Hash(installer), ReviewedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("官方安装器校验失败，未写入本地缓存。");
        var directory = Path.Combine(InstallerCacheDirectory, ReviewedTag);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, InstallerName);
        var temp = target + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(installer, temp, false);
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static ReleaseInfo ParseRelease(string json)
    {
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new IOException("GitHub 返回了草稿或预发布版本，未下载。");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (tag != ReviewedTag)
            throw new IOException("返回的安装器版本为 " + tag + "，与已核验版本 " + ReviewedTag + " 不一致。");
        var assets = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == InstallerName).ToArray();
        if (assets.Length != 1) throw new IOException("未找到唯一的官方安装器资源。");
        var a = assets[0];
        var url = a.GetProperty("browser_download_url").GetString() ?? "";
        if (url != Repository + "/releases/download/" + tag + "/" + InstallerName)
            throw new IOException("下载地址不属于指定项目的官方版本资源。");
        var digest = a.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
            throw new IOException("官方资源缺少 SHA-256 校验值，已停止下载。");
        long size = a.GetProperty("size").GetInt64();
        if (size < 1024 || size > 128 * 1024 * 1024) throw new IOException("安装器大小异常。");
        if (size != ReviewedInstallerSize || !string.Equals(digest[7..], ReviewedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("官方安装器资源已变化，与已核验的大小或 SHA-256 不一致，请更新助手。");
        return new ReleaseInfo(tag, url, digest[7..].ToLowerInvariant(), size);
    }

    public static async Task<ReleaseInfo> GetReleaseAsync(HttpClient client, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/danielblnc/DLSS-NR-on-AMD/releases/tags/" + ReviewedTag);
        request.Headers.UserAgent.ParseAdd("AMD-NR-Assistant/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(token));
    }

    public static async Task DownloadAsync(HttpClient client, ReleaseInfo release, string output,
        IProgress<int> progress, CancellationToken token)
    {
        var partial = output + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("下载没有使用 HTTPS。");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > release.Size) throw new IOException("下载大小超出官方记录。");
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    progress.Report((int)(total * 100 / release.Size));
                }
                if (total != release.Size) throw new IOException("安装器下载不完整。");
            }
            token.ThrowIfCancellationRequested();
            if (Hash(partial) != release.Sha256) throw new IOException("安装器 SHA-256 校验失败，未放入游戏目录。");
            File.Move(partial, output, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    static void Save(string path, Journal journal)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temp, path, true);
    }

    // Owns exactly two preparation files. Never guesses the upstream installer's proxy/config names.
    public static Journal Stage(string directory, string installer, string dll, string state, string tag,
        Action<int>? beforeCopy = null)
    {
        directory = Path.GetFullPath(directory);
        RejectLinks(directory);
        RejectLinks(state);
        Directory.CreateDirectory(state);
        if (File.Exists(Path.Combine(state, "journal.json"))) throw new IOException("准备记录已存在，请使用新的事务目录。");
        var pairs = new[] { (InstallerName, installer), (DllName, dll) };
        var changes = new List<Change>();
        for (int i = 0; i < pairs.Length; i++)
        {
            var (name, source) = pairs[i];
            var target = Path.Combine(directory, name);
            RejectLinks(target);
            if (Directory.Exists(target)) throw new IOException("目标文件名被文件夹占用：" + name);
            var next = Hash(source);
            var exists = File.Exists(target);
            var original = exists ? Hash(target) : "";
            var changed = next != original;
            if (exists && changed) File.Copy(target, Path.Combine(state, name + ".backup"), false);
            // Freeze source bytes, including when source DLL already lives in the game directory.
            File.Copy(source, Path.Combine(state, name + ".staged"), false);
            if (Hash(Path.Combine(state, name + ".staged")) != next) throw new IOException("源文件在复制时发生变化。");
            changes.Add(new Change(name, exists, original, next, changed));
        }
        var journal = new Journal(directory, tag, "prepared", changes);
        var jp = Path.Combine(state, "journal.json");
        Save(jp, journal);
        try
        {
            for (int i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                if (!change.Changed) continue;
                beforeCopy?.Invoke(i);
                var target = Path.Combine(directory, change.Name);
                RejectLinks(target);
                if (change.Existed ? !File.Exists(target) || Hash(target) != change.OriginalHash : File.Exists(target))
                    throw new IOException("目标文件已被其他程序修改：" + change.Name);
                var temp = Path.Combine(directory, ".amd-nr-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.Copy(Path.Combine(state, change.Name + ".staged"), temp, false);
                    File.Move(temp, target, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            journal = journal with { Phase = "staged" };
            Save(jp, journal);
            return journal;
        }
        catch (Exception error)
        {
            try
            {
                if (!File.Exists(Path.Combine(state, "journal.json")))
                {
                    foreach (var c in changes)
                    {
                        var backup = Path.Combine(state, c.Name + ".backup");
                        var staged = Path.Combine(state, c.Name + ".staged");
                        if (File.Exists(backup)) File.Delete(backup);
                        if (File.Exists(staged)) File.Delete(staged);
                    }
                }
                else Rollback(state);
            }
            catch (Exception rollback) { throw new IOException(error.Message + "；自动恢复未完成：" + rollback.Message, error); }
            throw;
        }
    }

    public static void Rollback(string state)
    {
        RejectLinks(state);
        var jp = Path.Combine(state, "journal.json");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(jp)) ?? throw new IOException("恢复记录无效。");
        if (journal.Phase is "installer-launched" or "installer-returned")
            throw new IOException("已运行官方安装器，必须使用官方 R 移除；不能把准备文件恢复当作插件卸载。");
        RejectLinks(journal.Target);
        // Preflight every file before touching any of them.
        foreach (var c in journal.Changes)
        {
            if (c.Name != InstallerName && c.Name != DllName) throw new IOException("恢复记录包含未知文件。");
            var target = Path.Combine(journal.Target, c.Name);
            RejectLinks(target);
            if (!c.Changed) continue;
            var current = File.Exists(target) ? Hash(target) : "";
            if (current != c.NewHash && current != c.OriginalHash) throw new IOException("文件已被其他程序修改，保留现场：" + c.Name);
            if (c.Existed && Hash(Path.Combine(state, c.Name + ".backup")) != c.OriginalHash)
                throw new IOException("备份文件校验失败：" + c.Name);
        }
        foreach (var c in journal.Changes.AsEnumerable().Reverse())
        {
            if (!c.Changed) continue;
            var target = Path.Combine(journal.Target, c.Name);
            if (File.Exists(target) && Hash(target) == c.NewHash)
            {
                if (c.Existed) File.Copy(Path.Combine(state, c.Name + ".backup"), target, true);
                else File.Delete(target);
            }
        }
        Save(jp, journal with { Phase = "rolled-back" });
    }

    public static void MarkPhase(string state, string phase)
    {
        var path = Path.Combine(state, "journal.json");
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ?? throw new IOException("准备记录无效。");
        Save(path, j with { Phase = phase });
    }

    public static void StageAlternative(string directory, string packageDirectory,
        string dll, string weights, string state)
    {
        directory = Path.GetFullPath(directory);
        packageDirectory = Path.GetFullPath(packageDirectory);
        RejectLinks(directory);
        RejectLinks(packageDirectory);
        ValidateDll(dll);

        var opti = Directory.EnumerateFiles(packageDirectory, "OptiScaler.dll", SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new IOException("所选目录缺少 OptiScaler.dll。");
        var ini = Directory.EnumerateFiles(packageDirectory, "OptiScaler.ini", SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new IOException("所选目录缺少 OptiScaler.ini。");
        RejectLinks(opti); RejectLinks(ini); RejectLinks(weights);
        CheckPe(opti, true);
        if (!string.Equals(Path.GetDirectoryName(opti), Path.GetDirectoryName(ini), StringComparison.OrdinalIgnoreCase))
            throw new IOException("OptiScaler.dll 与配置必须在同一包目录。");
        if (!BinaryContains(opti, "amd-presr") || !BinaryContains(opti, "dlssnr_amd"))
            throw new IOException("该包不是已识别的 OptiScaler AMD Pre-SR 构建（缺少 amd-presr / dlssnr_amd 标识）。");
        if (!string.Equals(Path.GetFileName(weights), "dlssnr_on_amd_weights.bin", StringComparison.OrdinalIgnoreCase) ||
            new FileInfo(weights).Length < 1024 * 1024)
            throw new IOException("权重文件名称或大小异常。");

        var packageRoot = Path.GetDirectoryName(opti)!;
        if (Directory.EnumerateDirectories(packageRoot).Any())
            throw new IOException("当前仅支持平铺 AMD Pre-SR 包；含子目录的包尚未验证，未写入游戏。");
        var sources = Directory.EnumerateFiles(packageRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(p => !string.Equals(Path.GetFileName(p), "nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(Path.GetFileName(p), "dlssnr_on_amd_weights.bin", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Relative: Path.GetRelativePath(packageRoot, p), Source: p)).ToList();
        sources = sources.Where(f => Path.GetExtension(f.Relative).Equals(".dll", StringComparison.OrdinalIgnoreCase) || f.Relative.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var f in sources)
        {
            RejectLinks(f.Source);
            if (!GameManagement.IsTracked(f.Relative)) throw new IOException("包中有未支持的组件：" + f.Relative);
            if (Path.GetExtension(f.Relative).Equals(".dll", StringComparison.OrdinalIgnoreCase)) CheckPe(f.Source, true);
        }
        if (!sources.Any(f => ProxyNames.Contains(f.Relative, StringComparer.OrdinalIgnoreCase)))
            sources.Add(("dxgi.dll", opti));
        sources.Add((DllName, dll));
        sources.Add(("dlssnr_on_amd_weights.bin", weights));

        Directory.CreateDirectory(state);
        var changes = new List<AlternativeChange>();
        foreach (var item in sources)
        {
            if (item.Relative.StartsWith("..") || Path.IsPathRooted(item.Relative))
                throw new IOException("模式二包包含无效路径。");
            var target = Path.Combine(directory, item.Relative);
            RejectLinks(target);
            RejectLinks(item.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var existed = File.Exists(target);
            var original = existed ? Hash(target) : "";
            var next = Hash(item.Source);
            var backup = Path.Combine(state, "backup", item.Relative);
            if (existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, false);
            }
            changes.Add(new AlternativeChange(item.Relative, existed, original, next));
        }
        var manifestPath = Path.Combine(state, "optiscaler-fallback.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(new AlternativeJournal(directory, changes), JsonOptions));
        try
        {
            foreach (var item in sources)
                File.Copy(item.Source, Path.Combine(directory, item.Relative), true);
        }
        catch
        {
            RollbackAlternative(state);
            throw;
        }
    }

    public static void RollbackAlternative(string state)
    {
        var path = Path.Combine(state, "optiscaler-fallback.json");
        var journal = JsonSerializer.Deserialize<AlternativeJournal>(File.ReadAllText(path))
            ?? throw new IOException("模式二恢复记录无效。");
        foreach (var change in journal.Changes.AsEnumerable().Reverse())
        {
            var target = Path.Combine(journal.Target, change.RelativePath);
            var current = File.Exists(target) ? Hash(target) : "";
            if (current == change.OriginalHash) continue;
            if (current != change.NewHash)
                throw new IOException("文件已被其他程序修改，停止恢复：" + change.RelativePath);
            if (change.Existed)
                File.Copy(Path.Combine(state, "backup", change.RelativePath), target, true);
            else if (File.Exists(target))
                File.Delete(target);
        }
    }

    static bool BinaryContains(string path, string marker)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(marker);
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i <= bytes.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (bytes[i + j] != needle[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }
}
