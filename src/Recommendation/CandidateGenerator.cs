namespace Mu.Recommendation;

public static class CandidateGenerator
{
    public static RecommendationCandidate[] Generate(EnvironmentSnapshot e,SmartCompatibilityRule[] rules,ScoringWeights weights,string goal,DateTimeOffset now,string? scope=null)
    {
        var result=new List<RecommendationCandidate>();
        var bits=int.TryParse(e.Field("bitness").Value,out var b)?b:64;
        foreach(var id in RecommendationRuleSchema.Schemes.Where(id=>id is not ("compat" or "amd-magpie" or "legacy") && (scope==null||id==scope)))
        {
            var matches=rules.Where(r=>r.Scheme==id&&CompatibilityRuleEngine.Match(r,e,now)&&r.Parameters.Passes<9)
                .OrderByDescending(r=>CompatibilityRuleEngine.Priority(r,e)).ThenByDescending(r=>r.VerifiedAt).ToArray();
            var applicable=matches.Length>0?matches.Cast<SmartCompatibilityRule?>():new SmartCompatibilityRule?[]{null};
            foreach(var rule in applicable)
            {
                var backend=rule?.Backend??(id=="author"?"mochizuki":id=="standard"?"none":id=="compat"&&e.Field("architecture").Value=="rdna3"?Mu.Neural.NeuralBackends.LmxxfRdna3:"daniel");
                var cap=CapabilityCatalog.For(id,bits,backend);
                var issues=HardConstraintEvaluator.Evaluate(e,cap,backend,rule,rules,now);
                ResolvedConfiguration config;
                try{config=ParameterResolver.Resolve(cap,e,rule,backend);}catch(InvalidDataException){continue;}
                var state=issues.Any(i=>i.State=="BLOCKED")?"BLOCKED":issues.Any(i=>i.State=="REQUIRE_ACTION")?"REQUIRE_ACTION":issues.Any(i=>i.State=="UNVERIFIED")||rule==null||rule.Validation=="unverified"?"UNVERIFIED":"ELIGIBLE";
                var ranking=state is "BLOCKED" or "REQUIRE_ACTION"?(Score:0d,Trusted:false,Level:state=="BLOCKED"?"已阻止":"需要处理"):RecommendationScoringEngine.Score(rule,e,goal,id,weights,now);
                var auto=id!="rx6000"&&state=="ELIGIBLE"&&(rule?.FailureCodes.Length??0)==0&&ranking.Trusted&&ranking.Score>=85&&rule?.Validation!="experimental";
                var reasons=new List<string>{rule==null?"没有当前游戏、构建和设备的完整验证规则。":rule.Description, "此分数是兼容证据评分，不是成功率或FPS预测。"};
                if(rule!=null)reasons.Add("依据："+rule.Source+" · "+rule.VerifiedAt.ToString("yyyy-MM-dd"));
                if(cap.External)reasons.Add(id=="base"?"外部管理：打开NeuralHub不代表游戏已配置。":"外置窗口处理，不向游戏目录部署注入文件。");
                result.Add(new(rule==null?"generic:"+id:"rule:"+rule.Id+":"+rule.Version,id,cap.Name,state,ranking.Score,ranking.Level,auto,cap.Premium,
                    rule?.Validation??"unverified",reasons.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(),issues,config,rule?.Id,rule?.Version,e.Fingerprint,cap.External));
            }
        }
        return result.OrderByDescending(x=>x.Automatic).ThenByDescending(x=>x.Score).ThenBy(x=>x.Id,StringComparer.Ordinal).ToArray();
    }
}
