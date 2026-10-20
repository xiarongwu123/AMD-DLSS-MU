using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Mu.Neural;

namespace AmdNrAssistant;

public sealed record NeuralOptions(string Backend = "auto", string GraphicsApi = "auto", string Proxy = "auto",
    int? Passes = null, double ModelScale = 1, bool Temporal = true, double HistoryStrength = 1,
    bool ApplyModel = true, int DynamicResolution = 1, bool? NrInline = null,
    bool? EarlyAnyModuleWrap = null, bool? AllowEnhancedBarriers = null);
public sealed record NeuralPlan(string GameId, string Backend, string GraphicsApi, string Proxy, int Passes,
    string Status, string RuleRevision, bool NrInline, bool EarlyAnyModuleWrap, bool AllowEnhancedBarriers,
    string[] Notices, NeuralOptions Options)
{
    public string StatusLabel => NeuralRules.StatusLabel(Status);
    public string ModeLabel => Passes switch { 1 => "标准", 2 => "增强", 3 => "高质量", <= 8 => "实验", _ => "实验 / 压力测试" };
}

public static class NeuralRenderPlanner
{
    public static void Validate(NeuralOptions o)
    {
        if (o.Backend != "auto" && !NeuralRules.Backends.Contains(o.Backend) ||
            o.GraphicsApi != "auto" && !NeuralRules.Apis.Contains(o.GraphicsApi) ||
            o.Proxy != "auto" && !NeuralRules.Proxies.Contains(o.Proxy) || o.Passes is < 1 or > 30 ||
            !double.IsFinite(o.ModelScale) || o.ModelScale is < .25 or > 1 ||
            !double.IsFinite(o.HistoryStrength) || o.HistoryStrength is < 0 or > 1 || o.DynamicResolution is < 0 or > 2)
            throw new IOException("神经渲染高级参数无效。");
    }
    public static string GpuSeries(string gpu)
    {
        var match = Regex.Match(gpu, @"\bRX\s*([679])\d{3}\b", RegexOptions.IgnoreCase);
        return match.Success ? "rx" + match.Groups[1].Value + "000" : "unknown";
    }
    public static string DetectApi(string exe)
    {
        using var stream = File.OpenRead(exe); using var pe = new PEReader(stream);
        var rva = pe.PEHeaders.PEHeader?.ImportTableDirectory.RelativeVirtualAddress ?? 0;
        if (rva == 0) return "unknown";
        var imports = new List<string>(); var data = pe.GetSectionData(rva).GetContent();
        for (int i = 0; i + 20 <= data.Length && i < 5120; i += 20)
        {
            var nameRva = BitConverter.ToInt32(data.AsSpan(i + 12, 4)); if (nameRva == 0) break;
            var bytes = pe.GetSectionData(nameRva).GetContent(); var length = 0;
            while (length < bytes.Length && length < 256 && bytes[length] != 0) length++;
            imports.Add(Encoding.ASCII.GetString(bytes.AsSpan(0, length)));
        }
        return ApiFromImports(imports);
    }
    public static string ApiFromImports(IEnumerable<string> imports)
    {
        var names = imports.ToHashSet(StringComparer.OrdinalIgnoreCase); var apis = new List<string>();
        if (names.Contains("d3d11.dll")) apis.Add("dx11");
        if (names.Contains("d3d12.dll")) apis.Add("dx12");
        if (names.Contains("vulkan-1.dll")) apis.Add("vulkan");
        return apis.Count == 1 ? apis[0] : "unknown";
    }
    public static NeuralPlan Select(string gameId, string gpu, string detectedApi, NeuralRuleDocument? rules,
        NeuralOptions options, DateTimeOffset now)
    {
        Validate(options);
        var series = GpuSeries(gpu); var notices = new List<string>();
        if (series == "unknown") throw new IOException("未确认受支持的 AMD RX 6000/7000/9000 显卡，请检查显卡检测结果。");
        var api = options.GraphicsApi == "auto" ? detectedApi : options.GraphicsApi;
        if (!NeuralRules.Apis.Contains(api)) throw new IOException("未能唯一确认 DX11 / DX12 / Vulkan；请在高级设置中选择游戏实际接口。");
        NeuralGameRule? rule = null;
        if (rules != null)
        {
            NeuralRules.Validate(rules);
            if (rules.ExpiresUtc > now) rule = rules.Rules.SingleOrDefault(r => r.GameId == gameId && r.GpuSeries.Contains(series) && r.GraphicsApis.Contains(api));
            else notices.Add("远程兼容规则已过期，使用未验证的保守默认设置。");
        }
        else notices.Add("暂未取得远程兼容规则，使用未验证的保守默认设置。");
        var backend = options.Backend == "auto" ? rule?.Backend ?? "daniel" : options.Backend;
        var proxy = options.Proxy == "auto" ? rule?.Proxy ?? (api == "vulkan" ? "winmm.dll" : "dxgi.dll") : options.Proxy;
        var passes = options.Passes ?? rule?.Passes ?? 1;
        if (backend != "mochizuki" && passes > 3) throw new IOException("Daniel / lmxxf 仅支持 1–3 Pass；4–30 Pass 仅用于 Mochizuki 实验模式。");
        var followsRule = rule != null && backend == rule.Backend && proxy == rule.Proxy && passes == rule.Passes;
        var status = followsRule ? rule!.Status : backend == "mochizuki" && series == "rx9000" ? "experimental" : "unverified";
        if (backend is "mochizuki" or "lmxxf" && series != "rx9000")
        { status = "unverified"; notices.Add("该后端当前面向 RDNA4；此 RX 6000/7000 组合未经验证，可能无法运行。可在高级设置改用 Daniel。"); }
        if (options.Backend != "auto" || options.Proxy != "auto" || options.Passes != null || options.ModelScale != 1 ||
            !options.Temporal || options.HistoryStrength != 1 || !options.ApplyModel || options.DynamicResolution != 1 ||
            options.NrInline != null || options.EarlyAnyModuleWrap != null || options.AllowEnhancedBarriers != null)
        { status = status is "verified" or "community" ? "unverified" : status; notices.Add("已手动修改参数，兼容状态不沿用原规则的验证结论。"); }
        if (passes >= 4) { status = "experimental"; notices.Add(passes >= 9 ? "9–30 Pass 为实验 / 压力测试，会显著增加 GPU 时间和显存占用，不代表更好画质。" : "4–8 Pass 为实验模式，画质和性能需自行验证。"); }
        if (api != "dx12") notices.Add("此接口使用到 DX12 的桥接路径；检测到接口不代表该游戏已稳定兼容。");
        if (backend == "mochizuki") notices.Add("首次编译神经网络可能需要数分钟；编译期间画面可能直通，不能据此判断已生效。");
        return new(gameId, backend, api, proxy, passes, status, rule == null ? "fallback" : rules!.Revision,
            options.NrInline ?? rule?.NrInline ?? false, options.EarlyAnyModuleWrap ?? rule?.EarlyAnyModuleWrap ?? false,
            options.AllowEnhancedBarriers ?? rule?.AllowEnhancedBarriers ?? false, notices.ToArray(), options);
    }
    public static Dictionary<string, string> Settings(NeuralPlan plan)
    {
        Validate(plan.Options);
        var result = new Dictionary<string, string> {
            ["Enabled"] = "true", ["RunBeforeSR"] = "true", ["NrBackend"] = plan.Backend,
            ["NrInline"] = plan.NrInline.ToString().ToLowerInvariant(),
            ["LmxxfEarlyAnyModuleWrap"] = plan.EarlyAnyModuleWrap.ToString().ToLowerInvariant(),
            ["LmxxfAllowEnhancedBarriers"] = plan.AllowEnhancedBarriers.ToString().ToLowerInvariant()
        };
        if (plan.Backend == "mochizuki")
        {
            result["MochizukiUnlockPasses"] = (plan.Passes > 3).ToString().ToLowerInvariant();
            result["MochizukiPasses"] = plan.Passes.ToString(); result["MochizukiMaxPasses"] = "0";
            result["MochizukiModelScale"] = plan.Options.ModelScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["MochizukiTemporal"] = plan.Options.Temporal.ToString().ToLowerInvariant();
            result["MochizukiHistoryStrength"] = plan.Options.HistoryStrength.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["MochizukiApplyModel"] = plan.Options.ApplyModel.ToString().ToLowerInvariant();
            result["MochizukiDynamicResolution"] = plan.Options.DynamicResolution.ToString();
        }
        else result["Passes"] = plan.Passes.ToString();
        return result;
    }
}
