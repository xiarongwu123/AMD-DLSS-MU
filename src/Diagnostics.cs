using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public record ActivityEvent(DateTime Utc, string Stage, string Result, string Detail);
public record LogExcerpt(string Name, string Status, long Bytes, DateTime? ModifiedUtc, string Text);

public static class Diagnostics
{
    static readonly object Gate = new();
    public static readonly string[] LogNames = ["dlssnr_on_amd.log", "OptiScaler.log", "ReShade.log", "dlss5-neural.log", "dlss5-feed.log"];
    public static string Redact(string text, string exe)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        foreach (var path in new[] { dir, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), GameManagement.Root }.OrderByDescending(p => p.Length))
            if (path.Length > 3) text = text.Replace(path, "<路径>", StringComparison.OrdinalIgnoreCase).Replace(path.Replace('\\', '/'), "<路径>", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?i)(?:[A-Z]:[\\/]|\\\\)[^\r\n\""<>]*", "<路径>", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"(?im)\b(token|password|authorization|api[_-]?key)\b\s*[:=]\s*[^\r\n]+", "$1=<已隐藏>", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "<邮箱>", RegexOptions.None, TimeSpan.FromSeconds(1));
        return text;
    }
    public static void Record(string exe, string stage, string result, string detail = "")
    {
        // Logging must never turn a successful restore/install into a failure.
        try
        {
            lock (Gate)
            {
                var folder = GameManagement.State(exe); Core.RejectLinks(folder); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "activity.json"); Core.RejectLinks(path);
                var entries = File.Exists(path) && new FileInfo(path).Length < 1024 * 1024
                    ? JsonSerializer.Deserialize<List<ActivityEvent>>(File.ReadAllText(path)) ?? new() : new List<ActivityEvent>();
                entries.Add(new(DateTime.UtcNow, stage, result, Redact(detail.Length > 6000 ? detail[..6000] : detail, exe)));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(entries.TakeLast(150), Core.JsonOptions));
                File.Move(temp, path, true);
            }
        }
        catch { /* Diagnostic export explicitly reports a missing activity journal. */ }
    }
    public static LogExcerpt ReadTail(string directory, string name, string exe, int limit = 64 * 1024)
    {
        var path = Path.Combine(directory, name);
        try
        {
            Core.RejectLinks(path);
            if (!File.Exists(path)) return new(name, "missing", 0, null, "未找到文件；不代表运行成功或失败。");
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = f.Length; f.Position = Math.Max(0, length - limit);
            var bytes = new byte[(int)Math.Min(length, limit)]; int read = 0;
            while (read < bytes.Length) { int n = f.Read(bytes, read, bytes.Length - read); if (n == 0) break; read += n; }
            var value = Encoding.UTF8.GetString(bytes, 0, read);
            if (length > limit && value.IndexOf('\n') is var newline && newline >= 0) value = value[(newline + 1)..];
            return new(name, length > limit ? "tail-truncated" : "read", length, File.GetLastWriteTimeUtc(path), Redact(value, exe));
        }
        catch (Exception e) { return new(name, "unreadable", 0, null, Redact(e.Message, exe)); }
    }
    public static string Export(string exe)
    {
        object summary;
        try { summary = JsonSerializer.Deserialize<JsonElement>(GameManagement.Diagnostic(exe)); }
        catch (Exception e) { summary = new { Error = Redact(e.ToString(), exe) }; }
        string runtime;
        try { runtime = GameManagement.Runtime(exe); } catch (Exception e) { runtime = e.Message; }
        var dir = Path.GetDirectoryName(exe)!;
        return JsonSerializer.Serialize(new
        {
            Schema = 2, Summary = summary, GameExe = Path.GetFileName(exe), Windows = Environment.OSVersion.ToString(),
            RuntimeObservation = Redact(runtime, exe),
            Activity = ReadTail(GameManagement.State(exe), "activity.json", exe, 256 * 1024),
            Logs = LogNames.Select(n => ReadTail(dir, n, exe)).ToArray(),
            Configs = new[] { "dlssnr_on_amd.ini", "OptiScaler.ini", "ReShade.ini", "dlss5-feed.cfg" }.Select(n => ReadTail(dir, n, exe)).ToArray(),
            Note = "包含脱敏日志片段与配置，日志可能来自之前的运行。自动脱敏不保证移除所有个人信息，请预览后再分享。不自动上传。"
        }, Core.JsonOptions);
    }
}
