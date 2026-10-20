using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Recommendation;
using Mu.Telemetry;
using Mu.Neural;
using Mu.Server.Data;
namespace Mu.Server.Services;

public static class RecommendationEvidence
{
    public static async Task<NeuralRuleDocument> AttachAsync(NeuralRuleDocument document,string game,AppDbContext db,DateTimeOffset now)
    {
        var cutoff=now.AddDays(-90).ToUnixTimeSeconds();
        var rows=await db.GameTelemetry.AsNoTracking().Where(x=>x.GameName==game&&x.MuMode=="recommendation"&&x.StartedAt>=cutoff)
            .OrderByDescending(x=>x.StartedAt).Take(10000).Select(x=>new{x.UserId,x.PayloadJson}).ToListAsync();
        var reports=new List<(string User,RecommendationObservation Data)>();
        foreach(var row in rows)try{var input=JsonSerializer.Deserialize<GameTelemetryUpload>(row.PayloadJson);if(input?.Recommendation is {} r)reports.Add((row.UserId,r));}catch(JsonException){}
        var smart=(document.SmartRules??[]).Where(r=>r.GameId==game).Select(rule=>{
            var valid=reports.Where(x=>x.Data.GameId==game&&x.Data.Scheme==rule.Scheme&&x.Data.Backend==rule.Backend&&
                x.Data.ComponentVersion==rule.ComponentVersion&&x.Data.PresetId=="rule:"+rule.Id+":"+rule.Version&&x.Data.VerifiedAt>=rule.VerifiedAt&&x.Data.VerifiedAt<=now&&
                (rule.BuildHashes.Length>0?rule.BuildHashes.Contains(x.Data.GameBuild,StringComparer.OrdinalIgnoreCase):rule.GameVersions.Contains(x.Data.GameVersion,StringComparer.OrdinalIgnoreCase))&&
                (rule.DeviceIds.Length==0||rule.DeviceIds.Contains(x.Data.GpuDeviceId,StringComparer.OrdinalIgnoreCase))&&
                (rule.GpuModels.Length==0||rule.GpuModels.Contains(x.Data.GpuModel,StringComparer.OrdinalIgnoreCase))&&
                (rule.Architectures.Length==0||rule.Architectures.Contains(x.Data.Architecture))&&x.Data.Api==rule.Api&&x.Data.Bitness==rule.Bitness&&
                (rule.DriverMin==null||Version.TryParse(x.Data.DriverVersion,out var v)&&v>=Version.Parse(rule.DriverMin))&&
                (rule.DriverMax==null||Version.TryParse(x.Data.DriverVersion,out var upper)&&upper<=Version.Parse(rule.DriverMax))&&
                !rule.BlockedDrivers.Contains(x.Data.DriverVersion))
                .GroupBy(x=>new{x.User,x.Data.DeviceId,Day=x.Data.VerifiedAt.UtcDateTime.Date})
                .Select(g=>g.OrderByDescending(x=>x.Data.VerifiedAt).First()).ToArray();
            var active=valid.Count(x=>x.Data.Active&&x.Data.Loaded&&x.Data.Installed&&x.Data.Crashed!=true&&x.Data.FailureCodes.Length==0);
            var unresolved=valid.Any(x=>x.Data.Crashed==true||x.Data.FailureCodes.Intersect(new[]{"ANTI_CHEAT","RUNTIME_EXIT","IMAGE_ABNORMAL","GAME_CRASH"}).Any());
            var evidence=new CommunityEvidence(valid.Select(x=>x.Data.DeviceId).Distinct().Count(),valid.Select(x=>x.User).Distinct().Count(),valid.Length,active,
                valid.Count(x=>x.Data.Installed),valid.Count(x=>x.Data.Loaded),valid.Count(x=>x.Data.Feedback=="acceptable"),Wilson(active,valid.Length),unresolved);
            return rule with{Community=evidence};
        }).ToArray();
        return document with{SmartRules=smart};
    }
    static double Wilson(int successes,int total){if(total==0)return 0;const double z=1.959963984540054;double p=(double)successes/total;return(p+z*z/(2*total)-z*Math.Sqrt((p*(1-p)+z*z/(4*total))/total))/(1+z*z/total);}
}
