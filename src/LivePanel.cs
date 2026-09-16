using System.Diagnostics;
using System.Reflection;
namespace AmdNrAssistant;

public static class LivePanel
{
    public const string RenoHash = "d5adf82eb44b065f4c590ac91fe824bab07afea0eb9f994bde936710c8593952";
    public static void SaveConfig(string exe, string path, string original, string updated)
    {
        GameManagement.EnsureClosed(exe); Core.RejectLinks(path);
        if (!new[] { "dlssnr_on_amd.ini", "OptiScaler.ini" }.Contains(Path.GetFileName(path)) ||
            Path.GetDirectoryName(Path.GetFullPath(path)) != Path.GetDirectoryName(Path.GetFullPath(exe)) || updated.Length > 1024 * 1024 || updated.Contains('\0')) throw new IOException("配置目标或内容无效。");
        if (File.ReadAllText(path) != original) throw new IOException("配置已被其他程序修改，请重新打开编辑器。");
        var folder = Path.Combine(GameManagement.State(exe), "config-" + Guid.NewGuid().ToString("N")); Core.RejectLinks(folder); Directory.CreateDirectory(folder);
        File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
        var temp = path + ".mu-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, updated); if (File.ReadAllText(path) != original) throw new IOException("配置发生并发变更，未保存。"); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Diagnostics.Record(exe, "config", "saved", Path.GetFileName(path));
    }
    public static void Install(string exe)
    {
        Core.ValidateGame(exe); GameManagement.EnsureClosed(exe);
        var check = GameManagement.Check(exe, false, false); if (check.Blocked) throw new IOException(string.Join("\n", check.Details));
        var dir = Path.GetDirectoryName(exe)!; var reno = Path.Combine(dir, "renodx-dlss5.addon64"); Core.RejectLinks(reno);
        if (!File.Exists(reno) || Core.Hash(reno) != RenoHash) throw new IOException("需要已安装的指定 RenoDX v4.7（SHA-256：" + RenoHash + "）。当前 AMD 模式一不提供此接口，不能安装后假装实时控制。");
        var hasReshade = Core.ProxyNames.Select(n => Path.Combine(dir, n)).Where(File.Exists).Any(p =>
        { Core.RejectLinks(p); var v = FileVersionInfo.GetVersionInfo(p); return (v.ProductName ?? "").Contains("ReShade", StringComparison.OrdinalIgnoreCase) && v.FileMajorPart == 6 && v.FileMinorPart == 8; });
        if (!hasReshade) throw new IOException("未识别 ReShade 6.8.x。需要 6.8.0 Add-on 版，面板不会自动安装或覆盖代理。");
        var target = Path.Combine(dir, "AMD-DLSS-MU.addon64"); Core.RejectLinks(target);
        if (File.Exists(target)) throw new IOException("实时面板已存在，请先恢复配置。");
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.LivePanel") ?? throw new IOException("此构建缺少原生面板资源。");
        var record = GameManagement.Read(exe);
        if (record is { Phase: not "installed" and not "restored" }) throw new IOException("请先恢复未完成的安装。");
        bool fresh = record == null || record.Phase == "restored";
        if (fresh) GameManagement.Begin(exe, 3, "RenoDX v4.7 live panel");
        try
        {
            using (var output = new FileStream(target, FileMode.CreateNew)) source.CopyTo(output);
            Core.CheckPe(target, true);
            GameManagement.RegisterAddon(exe, "AMD-DLSS-MU.addon64");
        }
        catch { if (File.Exists(target)) File.Delete(target); if (fresh) { GameManagement.Finish(exe, false); GameManagement.Restore(exe); } throw; }
    }
}
