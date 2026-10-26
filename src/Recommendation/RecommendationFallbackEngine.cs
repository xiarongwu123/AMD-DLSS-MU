namespace Mu.Recommendation;

public static class RecommendationFallbackEngine
{
    public static FallbackAdvice Diagnose(string[] failures,RecommendationCandidate? current,RecommendationCandidate[] candidates,SmartCompatibilityRule[] rules)
    {
        var code=failures.FirstOrDefault()??"RUNTIME_UNKNOWN";
        var stage=code switch{"GAME_PATH"=>"检测","DLL_CONFLICT" or "MISSING_RESOURCE" or "INTEGRITY_FAILED"=>"部署",_=>"运行验证"};
        if(code=="ANTI_CHEAT")return new(code,stage,"反作弊阻止；停止自动修复和注入。",[],true,["恢复之前配置","按游戏官方规则处理"]);
        var alternatives=candidates.Where(c=>c.Automatic&&c.Id!=current?.Id&&(!(code.StartsWith("NR_")||code=="MODEL_COMPILE_FAILED")||c.Scheme!="fps")&&c.Configuration.ComponentVersion==CapabilityCatalog.For(c.Scheme,c.Configuration.Bitness,c.Configuration.Backend).Version&&
            rules.Any(r=>r.Id==c.RuleId&&r.Fixes.Contains(code))).Select(c=>c.Id).ToArray();
        return new(code,stage,alternatives.Length>0?"找到有针对性验证记录的备选方案；切换前需要恢复并确认。":"没有可靠的针对性备选，停止自动尝试。",alternatives,true,
            ["查看错误详情","恢复之前配置"]);
    }
}
