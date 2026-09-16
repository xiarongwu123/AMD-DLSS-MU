using System.Net.Http;
using SharpCompress.Archives.SevenZip;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public static class OptiInstaller
{
    public const string Version = "0.9.4";
    public const string Url = "https://github.com/optiscaler/OptiScaler/releases/download/v0.9.4/Optiscaler_0.9.4-final.20260718._MM.7z";
    public const string Digest = "575cb4df866116093df75af607e37fd70e10f5163e0f23fd5c804142e80ef0ad";
    public const long Size = 55016448;
    public static readonly string[] Payload = ["OptiScaler.dll", "OptiScaler.ini", "libxess.dll", "libxess_dx11.dll", "libxess_fg.dll", "libxell.dll",
        "amd_fidelityfx_vk.dll", "amd_fidelityfx_dx12.dll", "amd_fidelityfx_upscaler_dx12.dll", "amd_fidelityfx_framegeneration_dx12.dll",
        "D3D12_Optiscaler/D3D12Core.dll", "Licenses/XeSS_LICENSE.txt", "Licenses/FidelityFX_v1_LICENSE.md", "Licenses/FidelityFX_v2_LICENSE.md", "Licenses/DirectX_LICENSE.txt"];
    public static string SetIni(string text, string section, string key, string value)
    {
        if (value.Contains('\n') || value.Contains('\r')) throw new IOException("无效配置值。");
        var lines = text.Replace("\r\n", "\n").Split('\n'); var found = -1; bool active = false; int sections = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { active = line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase); if (active) sections++; }
            else if (active && !line.StartsWith(';') && line.Contains('=') && line[..line.IndexOf('=')].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            { if (found >= 0) throw new IOException("配置含重复键：" + key); found = i; }
        }
        if (sections != 1 || found < 0) throw new IOException("配置不匹配，缺少唯一项：[" + section + "] " + key);
        lines[found] = key + "=" + value;
        return string.Join(text.Contains("\r\n") ? "\r\n" : "\n", lines);
    }
    public static string Configure(string text, string exe, bool vulkan)
    {
        text = SetIni(text, "Log", "LogToFile", "true"); text = SetIni(text, "Log", "LogLevel", "2");
        text = SetIni(text, "Menu", "OverlayMenu", "true"); text = SetIni(text, "Menu", "ShortcutKey", "0x2D");
        text = SetIni(text, "ProcessFilter", "TargetProcessName", Path.GetFileName(exe));
        return text;
    }
    public static void ExtractVerified(string archiveFile, string destination)
    {
        Core.RejectLinks(archiveFile); Core.RejectLinks(destination);
        if (new FileInfo(archiveFile).Length != Size || Core.Hash(archiveFile) != Digest) throw new IOException("OptiScaler 安装包校验失败。");
        using var archive = SevenZipArchive.OpenArchive(archiveFile);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToArray();
        if (entries.Length > 256) throw new IOException("安装包文件数异常。");
        foreach (var name in Payload)
        {
            var matches = entries.Where(e => (e.Key ?? "").Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].Size <= 0 || matches[0].Size > 256L * 1024 * 1024 || matches[0].LinkTarget != null)
                throw new IOException("安装包缺少唯一有效组件：" + name);
            var path = Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar)); Core.RejectLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = matches[0].OpenEntryStream(); using var output = new FileStream(path, FileMode.CreateNew);
            var buffer = new byte[81920]; long count = 0; int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { count += n; if (count > matches[0].Size) throw new IOException("解压长度异常。"); output.Write(buffer, 0, n); }
            if (count != matches[0].Size) throw new IOException("组件解压不完整：" + name);
        }
        foreach (var name in Payload.Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) Core.CheckPe(Path.Combine(destination, name), true);
    }
    public static async Task<string> PrepareAsync(string exe, IProgress<string> progress, CancellationToken token)
    {
        var cache = Path.Combine(Core.InstallerCacheDirectory, "OptiScaler-" + Version); Core.RejectLinks(cache); Directory.CreateDirectory(cache);
        var package = Path.Combine(cache, "package.7z"); Core.RejectLinks(package);
        if (!File.Exists(package) || new FileInfo(package).Length != Size || Core.Hash(package) != Digest)
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; client.DefaultRequestHeaders.UserAgent.ParseAdd("AMD-DLSS-MU/1.3.0");
            for (int attempt = 1; ; attempt++)
            {
                var partial = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    Diagnostics.Record(exe, "optiscaler-download", "start", "v" + Version + " attempt=" + attempt);
                    using var overall = CancellationTokenSource.CreateLinkedTokenSource(token); overall.CancelAfter(TimeSpan.FromMinutes(15));
                    using var headers = CancellationTokenSource.CreateLinkedTokenSource(token); headers.CancelAfter(TimeSpan.FromSeconds(30));
                    using var request = new HttpRequestMessage(HttpMethod.Get, attempt == 2 ? "https://api.github.com/repos/optiscaler/OptiScaler/releases/assets/481819753" : Url);
                    request.Headers.Accept.ParseAdd("application/octet-stream");
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token); response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("下载重定向不是 HTTPS。");
                    await using (var input = await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var bytes = new byte[81920]; long total = 0;
                        while (true)
                        {
                            using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token); idle.CancelAfter(TimeSpan.FromSeconds(60));
                            int n = await input.ReadAsync(bytes, idle.Token); if (n == 0) break;
                            total += n; if (total > Size) throw new IOException("下载大小超过已验证资源。");
                            await output.WriteAsync(bytes.AsMemory(0, n), token); progress.Report($"OptiScaler 下载 {100 * total / Size}%（第 {attempt} 次）");
                        }
                    }
                    if (new FileInfo(partial).Length != Size || Core.Hash(partial) != Digest) throw new IOException("安装包大小或 SHA-256 不匹配。");
                    File.Move(partial, package, true); break;
                }
                catch (Exception e) when (attempt < 3 && !token.IsCancellationRequested && (e is HttpRequestException || e is OperationCanceledException))
                { Diagnostics.Record(exe, "optiscaler-download", "retry", e.ToString()); await Task.Delay(attempt * 1000, token); }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
        }
        token.ThrowIfCancellationRequested(); progress.Report("正在校验并解压 OptiScaler…");
        var folder = Path.Combine(cache, "extract-" + Guid.NewGuid().ToString("N"));
        await Task.Run(() => ExtractVerified(package, folder), token); return folder;
    }
    public static void Install(string exe, string package, bool vulkan)
    {
        Core.ValidateGame(exe); GameManagement.EnsureClosed(exe);
        var dir = Path.GetDirectoryName(exe)!;
        var plan = Payload.Select(n => (Source: Path.Combine(package, n), Name: n == "OptiScaler.dll" ? (vulkan ? "winmm.dll" : "dxgi.dll") : n.Replace('/', Path.DirectorySeparatorChar))).ToArray();
        foreach (var f in plan)
        {
            Core.RejectLinks(f.Source); var target = Path.Combine(dir, f.Name); Core.RejectLinks(target);
            if (!File.Exists(f.Source) || !GameManagement.IsTracked(f.Name)) throw new IOException("组件无效：" + f.Name);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("请先恢复配置，已有组件：" + f.Name);
        }
        var ini = Configure(File.ReadAllText(Path.Combine(package, "OptiScaler.ini")), exe, vulkan);
        GameManagement.Begin(exe, 2, "OptiScaler " + Version);
        try
        {
            foreach (var f in plan)
            {
                var p = Path.Combine(dir, f.Name); Core.RejectLinks(p); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (f.Name == "OptiScaler.ini") { using var writer = new StreamWriter(new FileStream(p, FileMode.CreateNew)); writer.Write(ini); }
                else { File.Copy(f.Source, p, false); if (Core.Hash(p) != Core.Hash(f.Source)) throw new IOException("复制校验失败：" + f.Name); }
            }
            GameManagement.Finish(exe, true); Diagnostics.Record(exe, "optiscaler-install", "complete", "OptiScaler " + Version);
        }
        catch
        {
            GameManagement.Finish(exe, false); GameManagement.Restore(exe); throw;
        }
    }
}
