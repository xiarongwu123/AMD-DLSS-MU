namespace AmdNrAssistant;

public static class RenderSchemes
{
    public static readonly string[] Paid = ["high-quality", "author", "online", "rx6000", "game"];
    public static bool PremiumMode(int mode) => mode is 5 or 6 or 8;
    public static bool PremiumSelection(int mode, string scheme) => PremiumMode(mode) || Paid.Contains(scheme, StringComparer.Ordinal);
    public static string PreflightBackend(string scheme, int mode, string architecture, string requested)
    {
        if (scheme == "standard") return "none";
        if (mode != 6) return "daniel"; // Original modes never acquire the new host.
        var backend = requested != "auto" ? requested : scheme == "author" ? "mochizuki" : scheme is "compat" or "online" ? "auto" : "daniel";
        return backend != "auto" ? backend : scheme == "compat" && architecture == "rdna3" ? Mu.Neural.NeuralBackends.LmxxfRdna3 : "daniel";
    }
    public static void Validate(string scheme, int mode, bool x86)
    {
        if (scheme is "compat" or "amd-magpie" or "legacy") throw new IOException("此方案已移除，请选择其他图形方案。");
        if (scheme == "high-quality" && x86) throw new IOException("画质增强版仅包含64位运行组件。");
        if (scheme == "rx6000" && x86) throw new IOException("60系显卡专用使用64位 Daniel 0.6.0 组件；32位游戏请使用老游戏专用版。");
        if (scheme.Length == 0) return; // legacy callers still pass through mode authorization
        var expected = scheme switch
        {
            "base" or "rx6000" => 0, "fps" => 4, "high-quality" or "author" or "online" or "game" => 6,
            _ => throw new IOException("未知方案；AMD 大力喜鹊通过独立工具入口启动。")
        };
        if (mode != expected) throw new IOException("方案与配置模式不匹配。");
    }
    public const string Daniel08Release = "https://github.com/danielblnc/DLSS-NR-on-AMD/releases/tag/v0.8.0";
}
