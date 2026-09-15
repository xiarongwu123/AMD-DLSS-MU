using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AmdNrAssistant;

public record ManagedFile(string Name, string Before, string After);
public record GameRecord(string Exe, int Mode, string Version, DateTime InstalledUtc,
    string Phase, List<ManagedFile> Files);
public record Compatibility(bool Blocked, string Summary, string[] Details);

public static class GameManagement
{
    internal static string? StorageOverride { get; set; }
    public static string Root => StorageOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "games");
    public static string State(string exe) => Path.Combine(Root, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(exe).ToUpperInvariant()))));
    public static GameRecord? Read(string exe)
    {
        var file = Path.Combine(State(exe), "record.json");
        if (!File.Exists(file)) return null;
        Core.RejectLinks(file);
        var r = JsonSerializer.Deserialize<GameRecord>(File.ReadAllText(file)) ?? throw new IOException("安装记录损坏。");
        if (!string.Equals(Path.GetFullPath(r.Exe), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) throw new IOException("安装记录与游戏不匹配。");
        return r;
    }
    static void Save(GameRecord r)
    {
        var folder = State(r.Exe); Core.RejectLinks(folder); Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, "record.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(r, Core.JsonOptions));
        File.Move(temp, Path.Combine(folder, "record.json"), true);
    }
    public static readonly string[] Loaders = Core.ProxyNames.Concat(new[] { "d3d11.dll", "d3d12.dll", "nvngx.dll", "OptiScaler.dll", "dlss5-neural.addon64" }).Distinct().ToArray();
    public static bool IsTracked(string name) => Tracked(name);
    static bool Tracked(string name) => !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && (Loaders.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        name.Equals(Core.InstallerName, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvngx_dlssnr", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("dlssnr_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("OptiScaler", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("amd_fidelityfx", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libxe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("dlss-enabler-headless.dll", StringComparison.OrdinalIgnoreCase));
    static Dictionary<string, string> Inventory(string exe)
    {
        var dir = Path.GetDirectoryName(exe)!;
        return Directory.EnumerateFiles(dir).Where(p => Tracked(Path.GetFileName(p)))
            .ToDictionary(p => Path.GetFileName(p), p => { Core.RejectLinks(p); return Core.Hash(p); }, StringComparer.OrdinalIgnoreCase);
    }
    public static void Begin(string exe, int mode)
    {
        EnsureClosed(exe);
        if (Read(exe) is { Phase: not "restored" }) throw new IOException("此游戏已有安装记录，请先恢复，再切换或重新安装。");
        var files = Inventory(exe);
        var folder = State(exe); Core.RejectLinks(folder); Directory.CreateDirectory(folder);
        foreach (var f in files)
        {
            var backup = Path.Combine(folder, f.Key + ".backup"); Core.RejectLinks(backup);
            File.Copy(Path.Combine(Path.GetDirectoryName(exe)!, f.Key), backup, true);
            if (Core.Hash(backup) != f.Value) throw new IOException("备份校验失败。");
        }
        Save(new(exe, mode, mode == 0 ? Core.ReviewedTag : "AMD Pre-SR / local package (unverified)", DateTime.UtcNow, "installing", files.Select(f => new ManagedFile(f.Key, f.Value, f.Value)).ToList()));
    }
    public static void Finish(string exe, bool success)
    {
        var r = Read(exe) ?? throw new IOException("缺少安装记录。");
        var after = Inventory(exe);
        var before = r.Files.ToDictionary(f => f.Name, f => f.Before, StringComparer.OrdinalIgnoreCase);
        var names = before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase);
        Save(r with { Phase = success ? "installed" : "incomplete", Files = names.Select(n => new ManagedFile(n, before.GetValueOrDefault(n, ""), after.GetValueOrDefault(n, ""))).ToList() });
    }
    static string Target(string exe, string name)
    {
        if (Path.GetFileName(name) != name || !Tracked(name)) throw new IOException("记录包含非法路径。");
        var p = Path.Combine(Path.GetDirectoryName(exe)!, name); Core.RejectLinks(p); return p;
    }
    public static void Restore(string exe)
    {
        EnsureClosed(exe);
        var r = Read(exe) ?? throw new IOException("没有可恢复记录；旧版安装请使用原安装器卸载。");
        if (r.Phase == "installing") throw new IOException("检测到安装意外中断。无法确认外部安装器修改范围，请使用原安装器恢复并保留诊断记录。");
        // Check all files and all backups before the first mutation; a second attempt is safe.
        foreach (var f in r.Files)
        {
            var p = Target(exe, f.Name); var current = File.Exists(p) ? Core.Hash(p) : "";
            if (current != f.Before && current != f.After) throw new IOException("文件已被其他程序修改，未执行恢复：" + f.Name);
            if (f.Before.Length > 0)
            {
                var backup = Path.Combine(State(exe), f.Name + ".backup"); Core.RejectLinks(backup);
                if (!File.Exists(backup) || Core.Hash(backup) != f.Before) throw new IOException("备份损坏，未执行恢复：" + f.Name);
            }
        }
        foreach (var f in r.Files.Where(f => f.Before != f.After))
        {
            var p = Target(exe, f.Name);
            if (f.Before.Length > 0) File.Copy(Path.Combine(State(exe), f.Name + ".backup"), p, true);
            else if (File.Exists(p)) File.Delete(p);
        }
        Save(r with { Phase = "restored" });
    }
    public static DateTime? RunningSince(string exe)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (p)
            {
                try { if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return p.StartTime.ToUniversalTime(); }
                catch { throw new IOException("无法检查同名游戏进程；请退出游戏后重试。"); }
            }
        }
        return null;
    }
    public static void EnsureClosed(string exe) { if (RunningSince(exe) != null) throw new IOException("请先退出游戏再安装或恢复。"); }
    public static string[] Hardware()
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows()) return new[] { "非 Windows 系统：硬件检测不可用" };
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (root != null) foreach (var name in root.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
        {
            using var k = root.OpenSubKey(name);
            if (k?.GetValue("DriverDesc") is string desc) result.Add(desc + " | Driver " + k.GetValue("DriverVersion"));
        }
        return result.Count > 0 ? result.ToArray() : new[] { "未读取到显卡/驱动信息" };
    }
    public static Compatibility Check(string exe, bool checkConflicts = true)
    {
        var details = new List<string>(); bool blocked = false;
        try { Core.ValidateGame(exe); details.Add("EXE：Windows x64"); } catch (Exception e) { blocked = true; details.Add(e.Message); }
        var hardware = Hardware(); details.AddRange(hardware);
        if (hardware.Any(h => Regex.IsMatch(h, @"Radeon.*RX\s*[1-6]\d{3}", RegexOptions.IgnoreCase)) &&
            !hardware.Any(h => Regex.IsMatch(h, @"Radeon.*RX\s*[79]\d{3}", RegexOptions.IgnoreCase)))
        { blocked = true; details.Add("检测到的 Radeon RX 型号不在当前上游支持范围内。"); }
        if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { blocked = true; details.Add("当前运行时要求 Windows 11。"); }
        details.Add("驱动显示的是 Windows 驱动版本；未自动等同 Adrenalin 版本，最低要求需人工核对。");
        var dir = Path.GetDirectoryName(exe)!;
        var names = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToArray();
        if (names.Any(n => Regex.IsMatch(n ?? "", "easyanticheat|battleye|(^|_)eac|bedaisy|beclient", RegexOptions.IgnoreCase))) { blocked = true; details.Add("发现反作弊组件；不支持安装。"); }
        var proxy = Loaders.Where(n => File.Exists(Path.Combine(dir, n))).ToArray();
        var record = Read(exe);
        if (proxy.Length > 0) details.Add("发现插件/代理：" + string.Join(", ", proxy));
        if (checkConflicts && proxy.Length > 0 && (record == null || record.Phase == "restored")) { blocked = true; details.Add("存在未托管代理。请先使用其原安装器卸载，不能覆盖或自动删除。"); }
        if (record is { Phase: not "restored" }) details.Add($"已记录模式 {record.Mode + 1}，状态 {record.Phase}；切换前请恢复。");
        var evidence = names.Where(n => Regex.IsMatch(n ?? "", "d3d12|fidelityfx|fsr", RegexOptions.IgnoreCase)).ToArray();
        details.Add(evidence.Length > 0 ? "目录中发现 DX12/FSR 相关文件（不证明正在使用）：" + string.Join(", ", evidence) : "未找到 DX12/FSR 文件证据；可能静态链接或位于子目录。");
        details.Add("显卡兼容、实际图形接口和游戏版本尚未实测；RX 9000 为上游主要测试对象，RX 7000 需验证。");
        return new(blocked, blocked ? "不支持当前安装条件" : "未验证：基础检查通过", details.ToArray());
    }
    // Only lines observed after monitoring starts in this process session are eligible evidence.
    static readonly Dictionary<string, (DateTime Start, long Offset)> Watch = new(StringComparer.OrdinalIgnoreCase);
    public static string Runtime(string exe)
    {
        var since = RunningSince(exe);
        if (since == null) { Watch.Remove(exe); return "游戏未运行；神经渲染未验证。"; }
        var loaded = "插件加载未验证";
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))) using (process)
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) continue;
                if (process.Modules.Cast<ProcessModule>().Any(m => string.Equals(Path.GetDirectoryName(m.FileName), Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase) && Loaders.Contains(Path.GetFileName(m.FileName), StringComparer.OrdinalIgnoreCase)))
                    loaded = "已观察到游戏目录中的代理/插件加载（不证明神经渲染生效）";
            }
            catch { loaded = "进程模块不可读，插件加载未验证"; }
        }
        var log = Path.Combine(Path.GetDirectoryName(exe)!, "dlssnr_on_amd.log");
        var addonLog = Path.Combine(Path.GetDirectoryName(exe)!, "dlss5-neural.log");
        if (File.Exists(addonLog)) log = addonLog;
        Core.RejectLinks(log);
        long length = File.Exists(log) ? new FileInfo(log).Length : 0;
        if (!Watch.TryGetValue(exe, out var watch) || watch.Start != since || length < watch.Offset)
        { Watch[exe] = (since.Value, length); return loaded + "。已开始观察本次运行；请进入场景后再次刷新，旧日志不计入证据。"; }
        if (length == watch.Offset) return loaded + "。本次观察尚无新日志；神经渲染未验证。";
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = Math.Max(watch.Offset, stream.Length - 128 * 1024);
        using var reader = new StreamReader(stream); var text = reader.ReadToEnd();
        if (Regex.IsMatch(text, "invalid kernel|failed to load|device removed|hash mismatch|initialization failed", RegexOptions.IgnoreCase)) return "本次运行日志有错误：内核、组件加载或设备故障。请导出诊断。";
        if (log == addonLog && HasCompletedFrames(text)) return loaded + "。本次新增日志报告已处理帧（dlss5-neural 格式）；画质变化仍需游戏内对比。";
        // Runtime formats are version dependent: never infer inference success from menus or a generic timing line.
        return loaded + "。本次运行已产生日志；尚无已适配的推理成功证据，神经渲染未验证。";
    }
    public static bool HasCompletedFrames(string text) => Regex.IsMatch(text, @"\bframe [1-9][0-9]* processed \([0-9]+ skipped\)", RegexOptions.IgnoreCase);
    public static string Diagnostic(string exe)
    {
        var check = Check(exe, false); var record = Read(exe);
        var files = Inventory(exe);
        // Allowlisted error categories only: arbitrary log content, usernames and paths never leave the computer.
        var integrity = record?.Files.Select(f => new { Component = f.Name, MatchesInstalled = files.GetValueOrDefault(f.Name, "") == f.After }).ToArray();
        var categories = new List<string>();
        foreach (var name in new[] { "dlssnr_on_amd.log", "OptiScaler.log", "dlss5-neural.log" })
        {
            var p = Path.Combine(Path.GetDirectoryName(exe)!, name); if (!File.Exists(p)) continue; Core.RejectLinks(p);
            using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            f.Position = Math.Max(0, f.Length - 128 * 1024); using var reader = new StreamReader(f); var text = reader.ReadToEnd();
            foreach (var error in new[] { "invalid kernel", "failed to load", "hash mismatch", "device removed", "initialization failed" })
                if (text.Contains(error, StringComparison.OrdinalIgnoreCase)) categories.Add(name + ": " + error + " (日志历史，非本次生效证据)");
        }
        return JsonSerializer.Serialize(new { Schema = 1, AppVersion = typeof(GameManagement).Assembly.GetName().Version?.ToString(), CapturedUtc = DateTime.UtcNow,
            Hardware = Hardware(), Compatibility = check.Summary, Mode = record?.Mode + 1, record?.Version, record?.InstalledUtc, record?.Phase,
            Files = files.Select(f => new { Component = f.Key, Sha256 = f.Value }), Integrity = integrity, LogCategories = categories,
            Note = "不包含原始日志、游戏目录、用户名、设备序列号；导出不会上传。" }, Core.JsonOptions);
    }
}
