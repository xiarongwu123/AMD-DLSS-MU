namespace AmdNrAssistant;

public static class RenderSchemes
{
    public static readonly string[] Paid = ["compat", "author", "online", "amd-magpie", "legacy", "game"];
    public static bool PremiumMode(int mode) => mode is 5 or 6 or 8;
    public static void Validate(string scheme, int mode, bool x86)
    {
        if (scheme.Length == 0) return; // legacy callers still pass through mode authorization
        var expected = scheme switch
        {
            "base" => 0, "fps" => 4, "compat" or "author" or "online" or "game" => 6,
            "legacy" => x86 ? 5 : 8,
            _ => throw new IOException("未知方案；AMD 大力喜鹊通过独立工具入口启动。")
        };
        if (mode != expected) throw new IOException("方案与配置模式不匹配。");
    }
    public const string Daniel08Release = "https://github.com/danielblnc/DLSS-NR-on-AMD/releases/tag/v0.8.0";
}
