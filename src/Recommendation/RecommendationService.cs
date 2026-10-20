namespace Mu.Recommendation;

public static class RecommendationService
{
    public static RecommendationResult Evaluate(EnvironmentSnapshot environment,SmartCompatibilityRule[] rules,ScoringWeights weights,string revision,string source,
        DateTimeOffset expires,string goal,DateTimeOffset now,string? scope=null)
    {
        if(goal is not ("stability" or "framerate" or "quality"))throw new InvalidDataException("未知推荐目标。");
        RecommendationRuleSchema.Validate(weights);
        foreach(var rule in rules)RecommendationRuleSchema.Validate(rule);
        if(expires<=now)rules=[];
        var candidates=CandidateGenerator.Generate(environment,rules,weights,goal,now,scope);
        var best=candidates.FirstOrDefault(c=>c.Automatic);
        var free=candidates.FirstOrDefault(c=>!c.Premium&&c.Automatic);
        var notices=new List<string>();
        if(best==null)
        {
            notices.Add("当前游戏暂无已验证的自动配置；不会仅按显卡型号替你选择后端。");
            var issues=candidates.Where(c=>c.State!="BLOCKED").SelectMany(c=>c.Issues).DistinctBy(i=>i.Code).Take(6);
            notices.AddRange(issues.Select(i=>i.Message));
            if(!candidates.Any(c=>c.RuleId!=null))notices.Add("缺少匹配当前游戏构建、GPU、驱动与组件版本的有效实测规则。");
        }
        if(source=="cache")notices.Add("使用本机加密缓存的有效规则；安装仍遵守现有账户联网验证。");
        return new(Guid.NewGuid().ToString("N"),environment.GameId,environment.GameName,environment,goal,revision,source,expires,best,free,candidates,notices.ToArray());
    }
}
