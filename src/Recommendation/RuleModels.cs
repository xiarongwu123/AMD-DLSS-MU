using System.Text.RegularExpressions;
namespace Mu.Recommendation;

public sealed record SmartParameters(int Passes = 1, double ModelScale = 1, bool Temporal = true, double HistoryStrength = 1,
    bool ApplyModel = true, int DynamicResolution = 1, bool NrInline = false, bool EarlyAnyModuleWrap = false, bool AllowEnhancedBarriers = false);
public sealed record CommunityEvidence(int Devices, int Accounts, int Tests, int Active, int Installed, int Loaded, int Accepted,
    double WilsonLower, bool SevereIssues);
public sealed record ScoringWeights(double Environment = 40, double Verification = 35, double Freshness = 10, double Goal = 10, double Readiness = 5);
public sealed record SmartCompatibilityRule
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string GameId { get; init; } = "";
    public string[] GameVersions { get; init; } = [];
    public string[] BuildHashes { get; init; } = [];
    public string[] GpuModels { get; init; } = [];
    public string[] DeviceIds { get; init; } = [];
    public string[] Architectures { get; init; } = [];
    public string? DriverMin { get; init; }
    public string? DriverMax { get; init; }
    public string[] BlockedDrivers { get; init; } = [];
    public string Api { get; init; } = "dx12";
    public int Bitness { get; init; } = 64;
    public string Scheme { get; init; } = "compat";
    public string Backend { get; init; } = "daniel";
    public string ComponentVersion { get; init; } = "1.12.1";
    public string Proxy { get; init; } = "dxgi.dll";
    public long MinimumVramMb { get; init; }
    public long MinimumBudgetMb { get; init; }
    public string[] UpscalerInputs { get; init; } = [];
    public bool XeFgVerified { get; init; }
    public string[] Dependencies { get; init; } = [];
    public SmartParameters Parameters { get; init; } = new();
    public string Validation { get; init; } = "unverified";
    public string Source { get; init; } = "";
    public DateTimeOffset VerifiedAt { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public bool Disabled { get; init; }
    public bool Deny { get; init; }
    public bool ProtectedOnline { get; init; }
    public bool OnlineModificationAllowed { get; init; }
    public bool ExperimentalPasses { get; init; }
    public string[] KnownConflicts { get; init; } = [];
    public string[] FailureCodes { get; init; } = [];
    public string[] Fixes { get; init; } = [];
    public string Description { get; init; } = "";
    public CommunityEvidence? Community { get; init; }
}
public static class RecommendationRuleSchema
{
    public static readonly string[] Schemes = ["base", "fps", "author", "compat", "online", "amd-magpie", "legacy", "standard"];
    public static readonly string[] Apis = ["dx8", "dx9", "dx11", "dx12", "vulkan", "opengl"];
    public static readonly string[] Proxies = ["external", "dxgi.dll", "winmm.dll", "d3d12.dll", "winhttp.dll", "wininet.dll", "dbghelp.dll", "d3d9.dll", "opengl32.dll"];
    public static bool Id(string value) => value != null && Regex.IsMatch(value,"^[a-zA-Z0-9:_-]{1,100}$");
    public static void Validate(SmartCompatibilityRule r)
    {
        if (r == null || !Regex.IsMatch(r.Id??"","^[a-zA-Z0-9_-]{1,80}$") || !Regex.IsMatch(r.Version??"","^[a-zA-Z0-9._-]{1,40}$") || !Id(r.GameId) || !Schemes.Contains(r.Scheme) ||
            !new[]{"daniel","lmxxf","mochizuki","none"}.Contains(r.Backend) || !Apis.Contains(r.Api) ||
            r.Bitness is not (32 or 64) || !Proxies.Contains(r.Proxy) || r.ComponentVersion != CapabilityCatalog.For(r.Scheme,r.Bitness).Version ||
            r.MinimumVramMb is < 0 or > 1048576 || r.MinimumBudgetMb is < 0 or > 1048576 || r.UpscalerInputs==null || r.UpscalerInputs.Any(x=>x is not ("dlss" or "xess" or "fsr")) || r.Parameters == null || r.Parameters.Passes is < 1 or > 30 || r.Parameters.Passes > (r.Backend=="mochizuki" ? 30 : 3) ||
            !double.IsFinite(r.Parameters.ModelScale) || r.Parameters.ModelScale is < .25 or > 1 ||
            !double.IsFinite(r.Parameters.HistoryStrength) || r.Parameters.HistoryStrength is < 0 or > 1 || r.Parameters.DynamicResolution is < 0 or > 2 ||
            !new[]{"verified","community","experimental","unverified"}.Contains(r.Validation) ||
            r.VerifiedAt == default || r.ExpiresUtc <= r.VerifiedAt || r.Description == null || r.Description.Length > 2000 ||
            !Uri.TryCreate(r.Source,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0 ||
            r.Architectures == null || r.Architectures.Any(x=>x is not ("rdna2" or "rdna3" or "rdna4")) ||
            new[]{r.GameVersions,r.BuildHashes,r.GpuModels,r.DeviceIds,r.BlockedDrivers,r.Dependencies,r.KnownConflicts,r.FailureCodes,r.Fixes}
                .Any(a=>a==null || a.Length>100 || a.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>200||x.Any(char.IsControl))) ||
            r.BuildHashes.Any(x=>!Regex.IsMatch(x,"^[a-fA-F0-9]{64}$")) ||
            new[]{r.DriverMin,r.DriverMax}.Where(x=>x!=null).Any(x=>!Version.TryParse(x,out _)) ||
            r.Dependencies.Any(x=>!new[]{"hip","daniel-runtime","daniel-weights","mochizuki-model","lmxxf-model","amd-magpie-resources"}.Contains(x)))
            throw new InvalidDataException("智能推荐规则包含不支持的组件版本、匹配条件或参数。");
        var cap=CapabilityCatalog.For(r.Scheme,r.Bitness);
        if(!cap.Apis.Contains(r.Api)||!cap.Bitness.Contains(r.Bitness))throw new InvalidDataException("规则接口或位数不受现有安装器支持。");
        if(r.Scheme is "base" or "fps" or "online" or "amd-magpie" or "legacy" && r.Backend!="daniel" || r.Scheme=="standard"&&r.Backend!="none")
            throw new InvalidDataException("固定路线不能替换后端。");
        var fixedProxy=cap.External?"external":r.Scheme=="legacy"?r.Api=="opengl"?"opengl32.dll":r.Api is "dx8" or "dx9"?"d3d9.dll":"dxgi.dll":r.Scheme=="fps"?"dxgi.dll":r.Scheme=="standard"?r.Api=="vulkan"?"winmm.dll":"dxgi.dll":null;
        if(fixedProxy!=null&&r.Proxy!=fixedProxy)throw new InvalidDataException("代理DLL与当前固定安装路径不匹配。");
        if(r.Scheme is not ("author" or "compat" or "online")&&r.Parameters!=new SmartParameters())throw new InvalidDataException("此安装器不提供该高级参数接口。");
        if ((r.Parameters.Passes>3 && !r.ExperimentalPasses) || (r.Parameters.Passes>3 && r.Validation!="experimental"))
            throw new InvalidDataException("4–30 Pass 必须明确标记为实验规则。");
        if (r.Scheme=="author" && r.Backend!="mochizuki" || r.Scheme=="online" && r.Backend!="daniel")
            throw new InvalidDataException("方案后端与现有安装器不匹配。");
    }
    public static void Validate(ScoringWeights w)
    {
        var values=new[]{w.Environment,w.Verification,w.Freshness,w.Goal,w.Readiness};
        if(values.Any(x=>!double.IsFinite(x)||x<0)||Math.Abs(values.Sum()-100)>.001)throw new InvalidDataException("评分权重必须为非负且总计100。");
    }
}
public static class CapabilityCatalog
{
    public static SchemeCapability For(string id,int bits=64) => id switch {
        "base"=>new(id,"基础稳定版","0.8.0",false,true,["dx12"],[64]),
        "fps"=>new(id,"高帧率版","1.10.3.1+10.6",false,false,["dx12"],[64]),
        "author"=>new(id,"作者调校版","1.12.1",true,false,["dx11","dx12","vulkan"],[64]),
        "compat"=>new(id,"兼容版","1.12.1",true,false,["dx11","dx12","vulkan"],[64]),
        "online"=>new(id,"网游适配版","1.12.1",true,false,["dx11","dx12","vulkan"],[64]),
        "amd-magpie"=>new(id,"AMD 大力喜鹊","0.6.8.5-amd-rdna3",true,true,RecommendationRuleSchema.Apis,[32,64]),
        "legacy"=>new(id,"老游戏专用版",bits==32?"0.7.12/runtime0.6.0":"6.8.0/addon0.7.12/runtime0.6.0",true,false,bits==32?["dx8","dx9","dx11","opengl"]:["dx9","dx11","opengl"],[32,64]),
        "standard"=>new(id,"标准 OptiScaler","0.9.4",false,false,["dx11","dx12","vulkan"],[64]),
        _=>throw new InvalidDataException("未知技术方案。")};
}
