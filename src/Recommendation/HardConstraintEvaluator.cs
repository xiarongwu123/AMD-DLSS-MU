namespace Mu.Recommendation;

public static class HardConstraintEvaluator
{
    public static ConstraintIssue[] Evaluate(EnvironmentSnapshot e,SchemeCapability c,string backend,SmartCompatibilityRule? r,IEnumerable<SmartCompatibilityRule> all,DateTimeOffset now,bool manual=false)
    {
        var issues=new List<ConstraintIssue>();
        void Add(string code,string message,string state="REQUIRE_ACTION")=>issues.Add(new(code,message,state));
        var gpu=e.Field("gpuVendor");var arch=e.Field("architecture").Value;var api=e.Field("api");
        if(c.Id!="standard"&&gpu.Known&&gpu.Value!="AMD")Add("GPU_INCOMPATIBLE","此路线要求受支持的 AMD 显卡。","BLOCKED");
        else if(c.Id!="standard"&&!gpu.Known)Add("GPU_UNKNOWN","无法确认游戏使用的显卡。");
        if(c.Id!="standard"&&arch=="unknown")Add("ARCH_UNKNOWN","当前显卡架构不在已核验能力表内，不能按未知架构自动配置。");
        if(c.Id=="rx6000"&&arch!="rdna2")Add("ARCH_INCOMPATIBLE","60系显卡专用仅适用于 AMD RX 6000（RDNA2），请为当前显卡选择其他方案。","BLOCKED");
        if(c.Id=="rx6000"&&e.Field("bitness").Confirmed&&e.Field("bitness").Value!="64")Add("BITNESS_MISMATCH","Daniel 0.6.0 此路径使用64位组件；32位游戏请使用老游戏专用版。","BLOCKED");
        if((backend is "mochizuki" or "lmxxf")&&arch!="rdna4")Add("ARCH_INCOMPATIBLE","此后端的自动路径仅限已支持的 RDNA4。","BLOCKED");
        if(backend==Mu.Neural.NeuralBackends.LmxxfRdna3&&arch!="rdna3")Add("ARCH_INCOMPATIBLE","AMDNR lmxxf RDNA3 路线当前仅支持 RX 7000。","BLOCKED");
        if(backend==Mu.Neural.NeuralBackends.LmxxfRdna3&&c.Id!="compat")Add("SCHEME_INCOMPATIBLE","AMDNR RDNA3 资源包请通过兼容版配置。","BLOCKED");
        if(c.Id=="amd-magpie"&&arch!="rdna3")Add("ARCH_INCOMPATIBLE","当前 AMD 大力喜鹊是 RDNA3 实验路线。","BLOCKED");
        if(!e.Field("bitness").Confirmed)Add("BITNESS_UNKNOWN","无法确认游戏进程位数。");
        else if(!c.Bitness.Select(b=>b.ToString()).Contains(e.Field("bitness").Value))
        {
            if(e.Field("bitness").Value=="32")Add("BITNESS_UNVERIFIED","此方案尚无32位组件的验证记录，可手动选择；部署时会核对实际文件位数。","UNVERIFIED");
            else Add("BITNESS_MISMATCH","组件位数与游戏不匹配。","BLOCKED");
        }
        if(!c.External)
        {
            if(api.Known&&!c.Apis.Contains(api.Value))Add("API_INCOMPATIBLE","此安装路径不支持当前图形接口。","BLOCKED");
            else if(!api.Confirmed)Add("API_CONFIRM_REQUIRED","当前API仅为静态推断或未知，请按游戏实际设置确认。");
            if((e.AntiCheat.Length>0||r?.ProtectedOnline==true)&&!(r is {OnlineModificationAllowed:true,Validation:"verified"}))Add("ANTI_CHEAT",manual ? "检测到反作弊或受保护联机环境；只有明确知悉风险后才能手动继续，MU 不绕过反作弊。" : "受保护环境未明确允许修改，停止注入。",manual ? "UNVERIFIED" : "BLOCKED");
            if(e.Running)Add("GAME_RUNNING","请先关闭游戏再修改文件。");
            if(e.Conflicts.Length>0)Add("DLL_CONFLICT","存在未确认来源或已变更的代理DLL，不能覆盖："+string.Join("、",e.Conflicts));
            if(e.HasConfiguration)Add("RESTORE_REQUIRED","已有MU配置；切换前需要确认并恢复原配置。");
            if(!e.BackupReady)Add("BACKUP_UNAVAILABLE","无法确认可靠备份与恢复目录的写入权限。");
            if(e.IntegrityFailed)Add("INTEGRITY_FAILED","安装记录或文件校验失败。","BLOCKED");
        }
        if(c.Id=="amd-magpie"&&!e.Field("windowed").Confirmed)Add("WINDOW_MODE_UNKNOWN","请确认游戏已设置为窗口模式。");
        if(c.Id=="amd-magpie"&&e.Field("windowed").Value=="false")Add("WINDOW_MODE_REQUIRED","请将游戏改成窗口模式。");
        if(r is {MinimumVramMb:>0}&&(!long.TryParse(e.Field("vramMb").Value,out var vram)||!e.Field("vramMb").Confirmed||vram<r.MinimumVramMb))Add("VRAM_REQUIREMENT","专属规则要求的专用显存未满足或未确认。");
        if(r is {MinimumBudgetMb:>0}&&(!long.TryParse(e.Field("vramBudgetMb").Value,out var budget)||!e.Field("vramBudgetMb").Confirmed||budget<r.MinimumBudgetMb))Add("VRAM_BUDGET_UNKNOWN","此规则要求实际VRAM预算证据，目前无法确认。");
        if(r?.UpscalerInputs.Length>0&&(!e.Field("upscalerInput").Confirmed||!r.UpscalerInputs.Contains(e.Field("upscalerInput").Value)))Add("INPUT_MISMATCH","游戏输入未满足规则要求。");
        if(c.Id=="fps"&&r?.XeFgVerified!=true)Add("XEFG_VERSION_UNVERIFIED","缺少此游戏构建、组件组合的XeFG有效验证记录。","UNVERIFIED");
        if(c.Id=="fps"&&(!e.Field("upscalerInput").Confirmed||e.Field("upscalerInput").Value is not ("dlss" or "xess")))
            Add("XEFG_INPUT_UNKNOWN","尚未确认 XeFG 所需的 DLSS/XeSS 输入，不能直接启用补帧。");
        if(c.Id=="standard"&&(!e.Field("upscalerInput").Confirmed||e.Field("upscalerInput").Value is not ("dlss" or "xess" or "fsr")))
            Add("UPSCALER_INPUT_UNKNOWN","尚未确认游戏支持的超分输入。");
        foreach(var deny in all.Where(x=>x.Deny&&x.Scheme==c.Id&&CompatibilityRuleEngine.Match(x,e,now,true)))
            Add("RULE_DENIED",deny.Description.Length>0?deny.Description:"当前构建或驱动被明确禁止。","BLOCKED");
        if(all.Any(x=>x.Scheme==c.Id&&CompatibilityRuleEngine.Match(x,e,now)&&x.BlockedDrivers.Contains(e.Field("driver").Value)))Add("DRIVER_BLACKLIST","当前驱动处于此规则黑名单。","BLOCKED");
        if(r?.KnownConflicts.Any(x=>e.Components.ContainsKey(x))==true)Add("KNOWN_CONFLICT","检测到规则列出的冲突组件。","BLOCKED");
        var defaults=(backend==Mu.Neural.NeuralBackends.LmxxfRdna3?["hip","amdnr-rdna3-package"]:backend=="mochizuki"?["mochizuki-model"]:backend=="lmxxf"?["hip","lmxxf-model"]:
            c.Id=="amd-magpie"?["amd-magpie-resources"]:c.Id is "base" or "standard"?Array.Empty<string>():c.Id is "fps" or "rx6000"?["hip"]:c.Id=="legacy"?["hip"]:["hip","daniel-runtime","daniel-weights"]);
        var required=defaults.Concat(r?.Dependencies??[]).Distinct();
        foreach(var dep in required)if(!e.Resources.GetValueOrDefault(dep)&&!e.Resources.GetValueOrDefault(dep+"-preparable"))Add("MISSING_RESOURCE","需要准备并校验资源："+dep);
        if(r!=null&&r.ComponentVersion!=c.Version)Add("VERSION_MISMATCH","规则与组件固定版本不一致。","BLOCKED");
        if(!e.Field("driver").Known)Add("DRIVER_UNKNOWN","驱动版本未确认，无法排除版本冲突。");
        if(int.TryParse(e.Field("osBuild").Value,out var build)&&build<(c.Id is "amd-magpie" or "standard"||backend==Mu.Neural.NeuralBackends.LmxxfRdna3?19041:22000))Add("OS_VERSION","当前Windows构建低于固定运行库要求。","BLOCKED");
        if(e.Field("os").Value!="windows")Add("WINDOWS_REQUIRED","实际检测和部署需要Windows。","BLOCKED");
#if MU_NVIDIA_AUTHOR_TEST
        if (manual && c.Id == "author" && backend == "mochizuki" && gpu.Value.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            issues.RemoveAll(i => i.Code is "GPU_INCOMPATIBLE" or "ARCH_UNKNOWN" or "ARCH_INCOMPATIBLE");
            Add("NVIDIA_TEST_ONLY", "N卡预发布测试：仅验证资源与部署流程，不代表 AMD 后端可运行。", "UNVERIFIED");
        }
#endif
        return issues.ToArray();
    }
}
