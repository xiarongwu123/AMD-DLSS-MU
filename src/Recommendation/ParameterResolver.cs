using System.Globalization;
namespace Mu.Recommendation;

public static class ParameterResolver
{
    public static ResolvedConfiguration Resolve(SchemeCapability capability,EnvironmentSnapshot env,SmartCompatibilityRule? rule,string backend)
    {
        var p=rule?.Parameters??new();
        if(p.Passes>=9)throw new InvalidDataException("9–30 Pass 不参与自动推荐。");
        if(p.Passes>1&&(rule==null||rule.Validation is not ("verified" or "community" or "experimental")))
            throw new InvalidDataException("增加Pass需要验证记录。");
        var bits=int.TryParse(env.Field("bitness").Value,out var b)?b:64;
        var api=env.Field("api").Value;
        var proxy=rule?.Proxy??(capability.External?"external":capability.Id=="legacy"?api=="opengl"?"opengl32.dll":api is "dx8" or "dx9"?"d3d9.dll":"dxgi.dll":api=="vulkan"?"winmm.dll":"dxgi.dll");
        var ini=new Dictionary<string,Dictionary<string,string>>();
        if(capability.Id is "author" or "compat" or "online")
        {
            ini["DlssNr"]=new(){["Enabled"]="true",["RunBeforeSR"]="true",["NrBackend"]=backend,
                ["NrInline"]=p.NrInline.ToString().ToLowerInvariant(),["LmxxfEarlyAnyModuleWrap"]=p.EarlyAnyModuleWrap.ToString().ToLowerInvariant(),
                ["LmxxfAllowEnhancedBarriers"]=p.AllowEnhancedBarriers.ToString().ToLowerInvariant()};
            var nr=ini["DlssNr"];
            if(backend==Mu.Neural.NeuralBackends.LmxxfRdna3)
                ini["DlssNr"]=new(){["Enabled"]="true",["NrBackend"]="lmxxf",["Passes"]=p.Passes.ToString(),["AmdLmxxfFastMode"]="true",["AmdLmxxfTierSnap"]="true"};
            else if(backend=="mochizuki")foreach(var pair in new Dictionary<string,string>{["MochizukiUnlockPasses"]=(p.Passes>3).ToString().ToLowerInvariant(),
                ["MochizukiPasses"]=p.Passes.ToString(),["MochizukiMaxPasses"]="0",["MochizukiModelScale"]=p.ModelScale.ToString(CultureInfo.InvariantCulture),
                ["MochizukiTemporal"]=p.Temporal.ToString().ToLowerInvariant(),["MochizukiHistoryStrength"]=p.HistoryStrength.ToString(CultureInfo.InvariantCulture),
                ["MochizukiApplyModel"]=p.ApplyModel.ToString().ToLowerInvariant(),["MochizukiDynamicResolution"]=p.DynamicResolution.ToString()})nr[pair.Key]=pair.Value;
            else nr["Passes"]=p.Passes.ToString();
        }
        if(capability.Id=="fps")
        {
            ini["DlssNr"]=new(){["Enabled"]="true",["NrBackend"]="daniel",["Quality"]="1"};
            ini["FrameGen"]=new(){["Enabled"]="true",["External"]="false",["FGInput"]="upscaler",["FGOutput"]="xefg"};
            ini["XeFG"]=new(){["InterpolationCount"]="1"};
        }
        if(capability.Id=="legacy")ini["amd-nr"]=new(){["StartOn"]="1",["Scale"]="1.0",["ColourStrength"]="0.25",["Structure"]="1",["Skin"]="-1",["Passes"]="1"};
        return new(capability.Id,backend,capability.Version,proxy,bits,api,p.Passes,ini,p,rule?.Dependencies??[]);
    }
}
