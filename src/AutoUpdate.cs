using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public record AppRelease(string Tag, string Url, string Sha256, long Size, string Notes, string? ApiUrl = null);
public record UpdatePlan(string Target, int ProcessId, string OriginalHash, string NewHash, long Size, string Version);

public static class AutoUpdate
{
    public const string Website = "https://amd-dlss-mu.claude-api.cn";
    public const string ManifestUrl = Website + "/release.json";
    public const string DownloadUrl = Website + "/download/file";
    public const string AssetName = "AMD-DLSS-MU.exe";
    public static Version CurrentVersion => typeof(AutoUpdate).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string DisplayVersion => CurrentVersion.ToString(3);
    public static AppRelease? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var tag = root.GetProperty("tag").GetString() ?? "";
        var siteVersion = root.GetProperty("version").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") ||
            !Version.TryParse(tag[1..], out var version) || siteVersion != tag[1..])
            throw new IOException("官网发布版本格式不正确。");
        if (root.GetProperty("channel").GetString() != "stable" ||
            root.GetProperty("platform").GetString() != "Windows x64" ||
            root.GetProperty("file").GetString() != AssetName)
            throw new IOException("官网发布信息不是 Windows x64 正式版。");
        var normalized = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        if (version <= normalized) return null;
        var url = root.GetProperty("downloadUrl").GetString() ?? "";
        if (url != DownloadUrl || root.GetProperty("delivery").GetString() != "server")
            throw new IOException("官网更新文件必须由本站服务器提供。");
        var digest = root.GetProperty("sha256").GetString() ?? "";
        if (!Regex.IsMatch(digest, "^[a-fA-F0-9]{64}$")) throw new IOException("官网更新文件缺少有效 SHA-256。");
        var size = root.GetProperty("size").GetInt64();
        if (size < 1024 || size > 1024L * 1024 * 1024) throw new IOException("新版文件大小异常。");
        var notes = root.TryGetProperty("notes", out var body) ? body.GetString() ?? "" : "";
        return new(tag, url, digest.ToLowerInvariant(), size, notes[..Math.Min(notes.Length, 4000)]);
    }
    public static async Task<AppRelease?> CheckAsync(CancellationToken token)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AMD-DLSS-MU/" + DisplayVersion);
        return Parse(await client.GetStringAsync(ManifestUrl, token), CurrentVersion);
    }
    public static async Task<string> DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token, Action<string>? status = null)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "updates", Guid.NewGuid().ToString("N"));
        Core.RejectLinks(folder); Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, "download.partial"); var result = Path.Combine(folder, "candidate.exe");
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(client, release.Url, release.ApiUrl, release.Sha256,
                release.Size, partial, progress, token, status);
            DownloadSources.CurrentSession.Value?.Report("正在校验", 100, release.Size, "核对 EXE 架构与内部版本");
            await Task.Run(() => Verify(partial, release.Sha256, release.Size, release.Tag[1..]), token);
            token.ThrowIfCancellationRequested(); File.Move(partial, result);
            DownloadSources.CurrentSession.Value?.Report("已完成 · 校验通过", 100, release.Size, "SHA-256、EXE 架构与版本校验通过");
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var session = DownloadSources.CurrentSession.Value;
            session?.Report("下载失败", session.Percent, release.Size, e.Message);
            throw;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    static void Verify(string path, string hash, long size, string version)
    {
        Core.RejectLinks(path);
        if (new FileInfo(path).Length != size || !string.Equals(Core.Hash(path), hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("更新包哈希或大小校验失败。");
        Core.CheckPe(path, false);
        var info = FileVersionInfo.GetVersionInfo(path);
        var actual = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        if (actual != Version.Parse(version)) throw new IOException("EXE 内部版本与发布标签不匹配。");
    }
    public static string Prepare(string candidate, AppRelease release)
    {
        var target = Environment.ProcessPath ?? throw new IOException("无法确定当前 EXE。");
        if (!Path.GetFileName(target).Equals(AssetName, StringComparison.OrdinalIgnoreCase)) throw new IOException("请运行名为 AMD-DLSS-MU.exe 的 Windows 发布版进行更新。");
        Core.RejectLinks(target);
        Verify(candidate, release.Sha256, release.Size, release.Tag[1..]);
        // Check write access before shutting down. Never request elevation automatically.
        var probe = Path.Combine(Path.GetDirectoryName(target)!, ".amd-update-probe-" + Guid.NewGuid().ToString("N"));
        using (File.Open(probe, FileMode.CreateNew, FileAccess.Write)) { }
        File.Delete(probe);
        var folder = Path.GetDirectoryName(candidate)!;
        var helper = Path.Combine(folder, "updater.exe");
        var originalHash = Core.Hash(target); File.Copy(target, helper, false);
        if (Core.Hash(helper) != originalHash) throw new IOException("更新助手校验失败。");
        var plan = new UpdatePlan(target, Environment.ProcessId, originalHash, release.Sha256, release.Size, release.Tag[1..]);
        File.WriteAllText(Path.Combine(folder, "plan.json"), JsonSerializer.Serialize(plan));
        return helper;
    }
    public static void StartHelper(string helper)
    {
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(helper)! };
        start.ArgumentList.Add("--apply-update");
        using var p = Process.Start(start) ?? throw new IOException("无法启动更新助手。");
    }
    public static async Task ApplyAsync()
    {
        var helper = Environment.ProcessPath!; var folder = Path.GetDirectoryName(helper)!;
        Core.RejectLinks(folder);
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(Path.Combine(folder, "plan.json"))) ?? throw new IOException("更新记录无效。");
        var target = Path.GetFullPath(plan.Target); Core.RejectLinks(target);
        if (!Path.GetFileName(target).Equals(AssetName, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(target) == folder || Core.Hash(helper) != plan.OriginalHash) throw new IOException("更新目标无效。");
        var candidate = Path.Combine(folder, "candidate.exe");
        Verify(candidate, plan.NewHash, plan.Size, plan.Version);
        try
        {
            using var parent = Process.GetProcessById(plan.ProcessId);
            if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("待退出程序与更新目标不一致。");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await parent.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { /* Parent already exited. */ }
        if (Core.Hash(target) != plan.OriginalHash) throw new IOException("当前 EXE 已改变，未覆盖。");
        var id = Guid.NewGuid().ToString("N");
        var incoming = target + ".incoming-" + id;
        var backup = target + ".backup-" + id;
        var health = Path.Combine(folder, "healthy");
        File.Copy(candidate, incoming, false);
        Verify(incoming, plan.NewHash, plan.Size, plan.Version);
        bool replaced = false;
        Process? launched = null;
        try
        {
            File.Replace(incoming, target, backup); replaced = true;
            var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
            start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(health);
            launched = Process.Start(start) ?? throw new IOException("新版启动失败。");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline && !launched.HasExited)
            {
                if (File.Exists(health) && File.ReadAllText(health) == plan.NewHash) return;
                await Task.Delay(500);
            }
            throw new IOException("新版未完成启动，将恢复旧版。");
        }
        catch
        {
            if (replaced)
            {
                if (launched != null && !launched.HasExited) { launched.Kill(); await launched.WaitForExitAsync(); }
                if (Core.Hash(target) == plan.NewHash && Core.Hash(backup) == plan.OriginalHash)
                {
                    File.Replace(backup, target, Path.Combine(folder, "failed-version.exe"));
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
            }
            throw;
        }
        finally { launched?.Dispose(); if (File.Exists(incoming)) File.Delete(incoming); }
    }
    public static void ConfirmStartup(string? marker)
    {
        if (marker == null) return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "updates") + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(marker);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(full) != "healthy") return;
        Core.RejectLinks(full);
        File.WriteAllText(full, Core.Hash(Environment.ProcessPath!));
    }
}
