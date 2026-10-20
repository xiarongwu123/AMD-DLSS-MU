using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Mu.Neural;
using Mu.Recommendation;
using Mu.Telemetry;
using Mu.Server.Services;
using Mu.Server;
using Mu.Server.Data;
using Microsoft.EntityFrameworkCore;

internal static class NeuralRulesHttpTests
{
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        using var factory = new AccountFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var store = factory.Services.GetRequiredService<NeuralRuleStore>();
        Check((await client.GetAsync("/api/v1/render-rules?gameId=steam:1")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous cannot read paid game parameters");
        const string email = "neural-rules@example.test";
        await client.PostAsJsonAsync("/api/v1/auth/code", new { email, purpose = "register" });
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, code = factory.Mail.Codes[email+":register"], password = "correct horse battery", termsVersion = "2026-09-26", deviceName = "rules test" });
        var session = (await registration.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var denied = await client.GetAsync("/api/v1/render-rules?gameId=steam:1");
        Check(denied.StatusCode == HttpStatusCode.Forbidden && !(await denied.Content.ReadAsStringAsync()).Contains("\"rules\""), "Standard account receives no private parameters");
        async Task Membership(bool active)
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == session.Account.Id);
            user.ProExpiresAt = DateTimeOffset.UtcNow.AddDays(active ? 1 : -1).ToUnixTimeSeconds();
            await db.SaveChangesAsync();
        }
        await Membership(true);
        var initial = await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?gameId=steam:1");
        Check(initial is { Revision: "unconfigured", Rules.Length: 0 }, "Unconfigured server must not invent tested compatibility");
        Check((await client.GetAsync("/api/v1/render-rules?gameId=../evil")).StatusCode == HttpStatusCode.BadRequest, "Invalid game ID accepted");
        var one = new NeuralGameRule("steam:1", "lmxxf", "dxgi.dll", ["rx9000"], ["dx12"], "community", EarlyAnyModuleWrap: true, AllowEnhancedBarriers: true, Title: "游戏专属配置", Description: "测试档案");
        var two = one with { GameId = "steam:2", Backend = "mochizuki", Status = "experimental" };
        var document = new NeuralRuleDocument(1, "test-1", DateTimeOffset.UtcNow.AddDays(1), [one, two]);
        await store.PublishAsync(JsonSerializer.Serialize(document, NeuralRules.Json), "unconfigured", "test-admin", "verified test data flow");
        var response = await client.GetAsync("/api/v1/render-rules?gameId=steam:1");
        var selected = (await response.Content.ReadFromJsonAsync<NeuralRuleDocument>())!;
        Check(selected.Rules.Length == 1 && selected.Rules[0].GameId == "steam:1" && selected.Rules[0].EarlyAnyModuleWrap, "Game filtering or rule parameters incorrect");
        Check(response.Headers.CacheControl?.NoStore == true, "Rules response unexpectedly cacheable");
        Check((await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?gameId=steam:3"))!.Rules.Length == 0, "Unmatched game received unrelated rule");
        var newer = document with { Revision = "test-2", Rules = [one with { Backend = "mochizuki", Status = "experimental" }] };
        await store.PublishAsync(JsonSerializer.Serialize(newer, NeuralRules.Json), "test-1", "test-admin", "test remote update");
        Check((await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?gameId=steam:1"))!.Revision == "test-2", "Rule update requires process restart");
        var rejected = false;
        try { await store.PublishAsync(JsonSerializer.Serialize(newer with { Revision = "test-3" }, NeuralRules.Json), "test-1", "test-admin", "stale editor"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && store.Read().Revision == "test-2", "Concurrent stale publication overwrote newer rules");
        Check((await client.GetAsync("/admin/neural-rules")).StatusCode == HttpStatusCode.Redirect, "Anonymous can edit rules");
        var smart=new SmartCompatibilityRule{Id="fixture-compat",Version="v1",GameId="steam:1",BuildHashes=[new string('A',64)],GpuModels=["fixture RX 7900 XT"],
            Architectures=["rdna3"],DriverMin="31.0.0.0",DriverMax="31.0.9.9",Scheme="compat",Backend="daniel",ComponentVersion="1.12.1",Proxy="dxgi.dll",Api="dx12",Bitness=64,
            Validation="verified",Source="https://example.test/fixture-only",VerifiedAt=DateTimeOffset.UtcNow.AddDays(-1),ExpiresUtc=DateTimeOffset.UtcNow.AddDays(1)};
        var smartDocument=newer with{Revision="smart-1",SmartRules=[smart,smart with{Id="fixture-other",GameId="steam:2"}]};
        await store.PublishAsync(JsonSerializer.Serialize(smartDocument,NeuralRules.Json),"test-2","test-admin","test smart schema");
        var modern=await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?format=2&gameId=steam:1");
        Check(modern!.SmartRules is {Length:1}&&modern.SmartRules[0].GameId=="steam:1","New API filters smart rules by game");
        Check(!(await client.GetStringAsync("/api/v1/render-rules?gameId=steam:1")).Contains("smartRules"),"Strict old clients do not receive new contract fields");
        var sample=new RecommendationObservation(new string('B',64),"steam:1",new string('A',64),"fixture RX 7900 XT","rdna3","31.0.1.0","dx12",64,"compat","daniel","1.12.1","rule:fixture-compat:v1","smart-1",true,false,false,false,null,[],"unknown",DateTimeOffset.UtcNow);
        async Task<HttpResponseMessage> Upload(RecommendationObservation observation)
        {
            var payload=new GameTelemetryUpload(Guid.NewGuid().ToString(),"steam:1",null,null,null,null,null,null,null,null,"recommendation","test",observation.VerifiedAt,observation.VerifiedAt,null,0,null,null,null,null,null,"recommendation-observation",observation);
            return await client.PostAsJsonAsync("/api/v1/telemetry/sessions",payload);
        }
        Check((await Upload(sample)).IsSuccessStatusCode,"Real observation stored without FPS or invented ACTIVE");
        Check((await Upload(sample)).IsSuccessStatusCode,"Second observation can be stored for deduplication");
        var evidence=(await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?format=2&gameId=steam:1"))!.SmartRules![0].Community!;
        Check(evidence is{Devices:1,Accounts:1,Tests:1,Installed:1,Active:0}&&evidence.WilsonLower==0,"Same account/device/day deduplicates; installed is not active success");
        Check((await Upload(sample with{Active=true,Loaded=true})).StatusCode==HttpStatusCode.BadRequest,"Unverifiable ACTIVE cannot inflate recommendation success");
        Check((await Upload(sample with{GpuModel=@"C:\Users\private\gpu"})).StatusCode==HttpStatusCode.BadRequest,"Sensitive full paths rejected from recommendation data");
        rejected=false;
        try{await store.PublishAsync(JsonSerializer.Serialize(smartDocument with{Revision="forged",SmartRules=[smart with{Community=new(99,99,99,99,99,99,99,1,false)}]},NeuralRules.Json),"smart-1","test-admin","forged counts");}
        catch(InvalidDataException){rejected=true;}
        Check(rejected,"Rule editor cannot fabricate community counts");
        var before=store.HistoryIds().ToHashSet();
        await store.PublishAsync(JsonSerializer.Serialize(smartDocument with{Revision="smart-disabled",SmartRules=[smart with{Disabled=true}]},NeuralRules.Json),"smart-1","test-admin","disable fixture");
        var history=store.HistoryIds().Single(id=>!before.Contains(id));
        Check((await client.GetFromJsonAsync<NeuralRuleDocument>("/api/v1/render-rules?format=2&gameId=steam:1"))!.SmartRules![0].Disabled,"Disabled state reaches clients");
        await store.RollbackAsync(history,"smart-disabled","test-admin","rollback fixture");
        Check(store.Read().Revision.StartsWith("rollback-")&&store.Read().SmartRules![0].VerifiedAt==smart.VerifiedAt&&!store.Read().SmartRules![0].Disabled,"Audited rollback restores rule without refreshing verification time");
        await Membership(false);
        Check((await client.GetAsync("/api/v1/render-rules?gameId=steam:1")).StatusCode == HttpStatusCode.Forbidden, "Expired membership cannot retrieve private parameters");
        Console.WriteLine($"Neural rules HTTP: {count} assertions passed.");
    }
}
