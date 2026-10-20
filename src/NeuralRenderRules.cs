using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mu.Neural;

// Shared by the service and desktop. Rules contain bounded data, never download
// locations, paths, arbitrary INI keys or commands.
public sealed record NeuralGameRule(string GameId, string Backend, string Proxy,
    string[] GpuSeries, string[] GraphicsApis, string Status = "unverified",
    int Passes = 1, bool NrInline = false, bool EarlyAnyModuleWrap = false,
    bool AllowEnhancedBarriers = false, string Scheme = "dlss5-neural-rendering",
    string? Title = null, string? Description = null);
public sealed record NeuralRuleDocument(int SchemaVersion, string Revision,
    DateTimeOffset ExpiresUtc, NeuralGameRule[] Rules, Mu.Recommendation.SmartCompatibilityRule[]? SmartRules = null, Mu.Recommendation.ScoringWeights? Weights = null);

public static class NeuralRules
{
    public static readonly string[] Backends = ["daniel", "lmxxf", "mochizuki"];
    public static readonly string[] Proxies = ["dxgi.dll", "winmm.dll", "d3d12.dll", "winhttp.dll", "wininet.dll", "dbghelp.dll"];
    public static readonly string[] Apis = ["dx11", "dx12", "vulkan"];
    public static readonly string[] Statuses = ["verified", "community", "experimental", "unverified"];
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static string StatusLabel(string status) => status switch
    { "verified" => "已验证", "community" => "社区验证", "experimental" => "实验性", _ => "未验证" };

    public static NeuralRuleDocument Parse(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("神经渲染规则过大。");
        var document = JsonSerializer.Deserialize<NeuralRuleDocument>(json, Json)
            ?? throw new InvalidDataException("神经渲染规则为空。");
        Validate(document); return document;
    }
    public static void Validate(NeuralRuleDocument d)
    {
        if (d.SchemaVersion != 1 || string.IsNullOrWhiteSpace(d.Revision) || d.Revision.Length > 80 ||
            d.Rules == null || d.Rules.Length > 2000 || d.ExpiresUtc == default)
            throw new InvalidDataException("神经渲染规则格式无效。");
        if(d.SmartRules is {Length:>2000})throw new InvalidDataException("智能规则数量过多。");
        foreach(var r in d.SmartRules??[])Mu.Recommendation.RecommendationRuleSchema.Validate(r);
        if((d.SmartRules??[]).Select(r=>r.Id).Distinct().Count()!=(d.SmartRules??[]).Length)throw new InvalidDataException("智能规则ID重复。");
        if(d.Weights!=null)Mu.Recommendation.RecommendationRuleSchema.Validate(d.Weights);
        foreach (var r in d.Rules)
        {
            if (r == null || r.GameId == null || !Regex.IsMatch(r.GameId, "^[a-z0-9:_-]{1,100}$") ||
                r.Scheme != "dlss5-neural-rendering" || r.Title?.Length > 100 || r.Description?.Length > 2000 ||
                !Backends.Contains(r.Backend) || !Proxies.Contains(r.Proxy) ||
                !Statuses.Contains(r.Status) || r.Passes < 1 || r.Passes > (r.Backend == "mochizuki" ? 30 : 3) ||
                r.GpuSeries is not { Length: > 0 and <= 3 } || r.GpuSeries.Any(g => g is not ("rx6000" or "rx7000" or "rx9000")) ||
                r.GraphicsApis is not { Length: > 0 and <= 3 } || r.GraphicsApis.Any(a => !Apis.Contains(a)))
                throw new InvalidDataException("兼容规则包含无效后端、设备范围或参数。");
            // No client may turn untested RDNA2/3 into a stable RDNA4 recommendation.
            if (r.Backend is "mochizuki" or "lmxxf" && r.GpuSeries.Any(g => g != "rx9000") && r.Status == "verified")
                throw new InvalidDataException("RDNA4 后端在 RX 6000/7000 上不能标为已验证。");
        }
        var identities = d.Rules.SelectMany(r => r.GpuSeries.SelectMany(g => r.GraphicsApis.Select(a => $"{r.GameId}/{g}/{a}")));
        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Count())
            throw new InvalidDataException("兼容规则的游戏、显卡和接口范围重叠。");
    }
}
