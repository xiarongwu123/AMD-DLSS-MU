using System.Text.Json;
using Mu.Neural;

namespace Mu.Server.Services;

public sealed class NeuralRuleStore(IWebHostEnvironment environment, IConfiguration config, TimeProvider clock)
{
    readonly SemaphoreSlim gate = new(1);
    string FilePath => Path.GetFullPath(config["NeuralRendering:RulesFile"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "neural-render-rules.json"));
    public NeuralRuleDocument Read()
    {
        var path = FilePath;
        if (!File.Exists(path)) return new(1, "unconfigured", clock.GetUtcNow().AddHours(1), []);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("兼容规则文件过大。");
        return NeuralRules.Parse(File.ReadAllText(path));
    }
    public string[] HistoryIds()
    {
        var folder=FilePath+".history";
        return Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").Select(Path.GetFileNameWithoutExtension).Where(x=>x!=null).Select(x=>x!).OrderDescending().Take(50).ToArray():[];
    }
    public async Task RollbackAsync(string id,string expectedRevision,string actor,string reason)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(id??"","^[0-9]{14}-[a-f0-9]{32}$"))throw new InvalidDataException("无效历史版本。");
        var path=Path.Combine(FilePath+".history",id+".json");
        if(!File.Exists(path)||new FileInfo(path).Length>3*1024*1024)throw new InvalidDataException("历史规则不存在或过大。");
        using var history=JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var previous=NeuralRules.Parse(history.RootElement.GetProperty("before").GetRawText());
        // A rollback is a new audited revision. Historical validation timestamps are not refreshed.
        var candidate=previous with{Revision="rollback-"+Guid.NewGuid().ToString("N"),ExpiresUtc=clock.GetUtcNow().AddDays(1),
            SmartRules=(previous.SmartRules??[]).Select(r=>r with{Community=null}).ToArray()};
        await PublishAsync(JsonSerializer.Serialize(candidate,NeuralRules.Json),expectedRevision,actor,reason);
    }
    public async Task PublishAsync(string json, string expectedRevision, string actor, string reason)
    {
        var candidate = NeuralRules.Parse(json);
        if((candidate.SmartRules??[]).Any(r=>r.Community!=null))throw new InvalidDataException("社区计数由真实监控记录计算，不可手工填写。");
        if (candidate.ExpiresUtc <= clock.GetUtcNow() || candidate.ExpiresUtc > clock.GetUtcNow().AddDays(30))
            throw new InvalidDataException("有效期必须在未来 30 天内。");
        await gate.WaitAsync();
        try
        {
            var previous = Read();
            foreach(var rule in candidate.SmartRules??[])
            {
                var old=(previous.SmartRules??[]).SingleOrDefault(r=>r.Id==rule.Id&&r.Version==rule.Version);
                if(old!=null)
                {
                    string Identity(Mu.Recommendation.SmartCompatibilityRule r)=>JsonSerializer.Serialize(r with{Disabled=false,Deny=false,ExpiresUtc=default,Community=null},NeuralRules.Json);
                    if(Identity(old)!=Identity(rule))throw new InvalidDataException("改变规则匹配条件、参数或验证依据时必须更新Rule Version。");
                }
            }
            if (previous.Revision != expectedRevision) throw new InvalidDataException("规则已被其他管理员更新，请重新加载页面。");
            if (candidate.Revision == previous.Revision) throw new InvalidDataException("请填写新的 revision。");
            var path = FilePath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var audit = path + ".history"; Directory.CreateDirectory(audit);
            // Keep full before/after plus actor/reason for review and manual rollback.
            var id = clock.GetUtcNow().ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(audit, id + ".json"), JsonSerializer.Serialize(new
            { actor, reason, at = clock.GetUtcNow(), before = previous, after = candidate }, NeuralRules.Json));
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(candidate, NeuralRules.Json)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
    }
}
