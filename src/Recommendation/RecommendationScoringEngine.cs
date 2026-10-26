namespace Mu.Recommendation;

public static class RecommendationScoringEngine
{
    public static double Wilson(int success,int total)
    {
        if(total<=0)return 0;const double z=1.959963984540054;var p=(double)success/total;
        return (p+z*z/(2*total)-z*Math.Sqrt((p*(1-p)+z*z/(4*total))/total))/(1+z*z/total);
    }
    public static bool CommunityTrusted(CommunityEvidence? c)=>c is {Devices:>=20,Accounts:>=20,Tests:>=30,WilsonLower:>=.8,SevereIssues:false};
    public static (double Score,bool Trusted,string Level) Score(SmartCompatibilityRule? rule,EnvironmentSnapshot e,string goal,string scheme,ScoringWeights w,DateTimeOffset now)
    {
        var exact=rule!=null&&CompatibilityRuleEngine.Exact(rule,e);
        var trusted=rule!=null&&((rule.Validation=="verified"&&exact)||rule.Validation=="community"&&CommunityTrusted(rule.Community));
        var match=rule==null?.1:exact?1:rule.GameVersions.Length+rule.BuildHashes.Length>0?.8:.5;
        var reliability=rule==null?0:rule.Validation=="verified"?(exact?1:.65):rule.Validation=="community"?Math.Min(.95,rule.Community?.WilsonLower??0):.1;
        var fresh=rule==null?0:Math.Clamp(1-(now-rule.VerifiedAt).TotalDays/90,0,1);
        var preference=goal=="framerate"?scheme=="fps"?1:.3:goal=="quality"?scheme=="author"?1:scheme is "base" or "compat"?.7:.3:scheme is "base" or "compat" or "standard"?1:.4;
        var score=Math.Round(w.Environment*match+w.Verification*reliability+w.Freshness*fresh+w.Goal*preference+w.Readiness*(e.Resources.Where(x=>x.Key.EndsWith("-preparable")).Any(x=>x.Value&&!e.Resources.GetValueOrDefault(x.Key[..^11]))?.5:1),1);
        // Unknown evidence must never acquire the appearance of a high-confidence recommendation.
        if(!trusted)score=Math.Min(score,69);
        return(score,trusted,score>=85&&trusted?"优先推荐":score>=70?"建议试用":score>=50?"实验候选":"证据不足");
    }
}
