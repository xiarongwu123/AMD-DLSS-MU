using AmdNrAssistant;
using Mu.Recommendation;
using Mu.Neural;
using System.Security.Cryptography;
using System.Text.Json;

static class RecommendationTests
{
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception("Smart recommendation: "+name);count++;}
    static readonly DateTimeOffset Now=new(2026,10,18,12,0,0,TimeSpan.Zero);
    static EnvironmentSnapshot Environment(string game="steam:1",string gpu="RX 9070 XT",string arch="rdna4",string build="A")=>new(){
        GameId=game,GameName="fixture",Fingerprint="fixture-environment",BackupReady=true,
        Fields=new(){["gpuVendor"]=new("AMD","CONFIRMED","fixture"),["gpuModel"]=new(gpu,"CONFIRMED","fixture"),["architecture"]=new(arch,"INFERRED","fixture"),
            ["driver"]=new("31.0.1.2","CONFIRMED","fixture"),["api"]=new("dx12","CONFIRMED","fixture"),["bitness"]=new("64","CONFIRMED","fixture"),
            ["gameVersion"]=new("1.0","CONFIRMED","fixture"),["gameBuild"]=new(new string(build[0],64),"CONFIRMED","fixture"),["os"]=new("windows","CONFIRMED","fixture"),
            ["osBuild"]=new("22631","CONFIRMED","fixture"),["upscalerInput"]=new("dlss","CONFIRMED","fixture"),["windowed"]=new("true","CONFIRMED","fixture")},
        Resources=new(){["hip"]=true,["daniel-runtime"]=true,["daniel-weights"]=true,["mochizuki-model"]=true,["lmxxf-model"]=true,["amd-magpie-resources"]=true}};
    static SmartCompatibilityRule Rule(string scheme="author",string gpu="RX 9070 XT",string arch="rdna4")=>new(){Id="fixture-"+scheme,Version="v1",GameId="steam:1",BuildHashes=[new string('A',64)],
        GpuModels=[gpu],Architectures=[arch],DriverMin="31.0.1.0",DriverMax="31.0.1.9",Scheme=scheme,Backend=scheme=="author"?"mochizuki":"daniel",
        ComponentVersion=CapabilityCatalog.For(scheme).Version,Api="dx12",Bitness=64,Proxy="dxgi.dll",Validation="verified",Source="https://example.test/real-test-fixture-not-production",
        VerifiedAt=Now.AddDays(-1),ExpiresUtc=Now.AddDays(5)};
    static RecommendationResult Evaluate(EnvironmentSnapshot e,params SmartCompatibilityRule[] rules)=>RecommendationService.Evaluate(e,rules,new(),"fixture","remote",Now.AddDays(1),"quality",Now);
    public static async Task RunAsync()
    {
        var env=Environment();var rule=Rule();
        var rx6000Env=Environment(gpu:"RX 6750 GRE",arch:"rdna2");
        var rx6000=Evaluate(rx6000Env).Candidates.Single(c=>c.Scheme=="rx6000");
        Check(rx6000.Premium&&!rx6000.External&&rx6000.Configuration.Backend=="daniel"&&rx6000.Configuration.ComponentVersion=="0.6.0","RX6000 is a paid fixed Daniel 0.6 game deployment");
        Check(!rx6000.Issues.Any(i=>i.Code=="MISSING_RESOURCE"),"RX6000 should acquire runtime/model through fixed official installer");
        Check(Evaluate(Environment(gpu:"RX 7900 XT",arch:"rdna3")).Candidates.Single(c=>c.Scheme=="rx6000").Issues.Any(i=>i.Code=="ARCH_INCOMPATIBLE"&&i.State=="BLOCKED"),"RX6000 route accepted RX7000");
        Check(Evaluate(rx6000Env with{Fields=new(rx6000Env.Fields){["bitness"]=new("32","CONFIRMED","PE")}}).Candidates.Single(c=>c.Scheme=="rx6000").State=="BLOCKED","RX6000 x64 installer accepted 32bit game");
        Check(Evaluate(rx6000Env with{Fields=new(rx6000Env.Fields){["api"]=new("dx11","CONFIRMED","fixture")}}).Candidates.Single(c=>c.Scheme=="rx6000").Issues.Any(i=>i.Code=="API_INCOMPATIBLE"),"RX6000 fixed DX12 route accepted DX11");
        var result=Evaluate(env,rule);
        Check(result.Recommended is {Scheme:"author",Automatic:true},"RDNA4 exact validated rule can recommend Mochizuki");
        Check(result.Recommended!.Configuration.Passes==1,"Mochizuki defaults to one pass");
        Check(!Evaluate(Environment(gpu:"RX 7900 XT",arch:"rdna3"),rule).Candidates.Any(x=>x.Scheme=="author"&&x.Automatic),"RDNA3 does not match RDNA4 rule");
        var daniel=Rule("compat","RX 7900 XT","rdna3");
        Check(Evaluate(Environment(gpu:"RX 7900 XT",arch:"rdna3"),rule,daniel).Recommended?.Scheme=="compat","same game different GPU chooses supported rule");
        Check(Evaluate(Environment(game:"steam:2"),rule).Recommended==null,"same GPU different game has no fabricated match");
        Check(Evaluate(Environment(build:"B"),rule).Recommended==null,"new game build invalidates prior rule");
        var blocked=rule with{BlockedDrivers=["31.0.1.2"]};
        Check(Evaluate(env,blocked).Candidates.Where(x=>x.Scheme=="author").All(x=>x.State=="BLOCKED"&&x.Score==0),"driver blacklist blocks before scoring");
        var facts=new Dictionary<string,Observation>(env.Fields){["bitness"]=new("32","CONFIRMED","PE"),["api"]=new("dx9","CONFIRMED","fixture")};
        var legacy=Evaluate(env with{Fields=facts});var bridge=legacy.Candidates.Single(x=>x.Scheme=="legacy");
        Check(bridge.Configuration.Bitness==32&&bridge.Configuration.Proxy=="d3d9.dll"&&bridge.Configuration.ComponentVersion.Contains("0.7.12"),"x86 DX9 uses 32bit bridge capability");
        var bitnessOnly = HardConstraintEvaluator.Evaluate(env with { Fields = new(env.Fields) { ["bitness"] = new("32","CONFIRMED","PE") } }, CapabilityCatalog.For("fps"), "daniel", null, [], Now, true);
        Check(bitnessOnly.Any(i=>i.Code=="BITNESS_UNVERIFIED"&&i.State=="UNVERIFIED")&&!bitnessOnly.Any(i=>i.Code=="BITNESS_MISMATCH"),"x86 is advisory for manual selection instead of a blanket block");
        Check(!legacy.Candidates.Single(x=>x.Scheme=="fps").Automatic,"unverified x86 components must not be auto-recommended");
        var manualOnline=HardConstraintEvaluator.Evaluate(env with{AntiCheat=["EasyAntiCheat"]},CapabilityCatalog.For("online"),"daniel",null,[],Now,true);
        Check(manualOnline.Any(i=>i.Code=="ANTI_CHEAT"&&i.State=="UNVERIFIED")&&!manualOnline.Any(i=>i.Code=="ANTI_CHEAT"&&i.State=="BLOCKED"),"manual anti-cheat warning must reach the explicit acknowledgement instead of permanent block");
        var missing=env with{Resources=new(){["hip"]=true}};
        Check(Evaluate(missing,rule).Recommended==null&&Evaluate(missing,rule).Candidates.Any(x=>x.Issues.Any(i=>i.Code=="MISSING_RESOURCE")),"missing runtime/model is require-action");
        Check(Evaluate(env with{Conflicts=["dxgi.dll"]},rule).Recommended==null,"unknown proxy never overwritten");
        Check(Evaluate(env with{AntiCheat=["EasyAntiCheat"]},rule).Candidates.Where(x=>!x.External).All(x=>x.State=="BLOCKED"),"protected game blocks injection without explicit permission");
        Check(Evaluate(env with{Running=true},rule).Recommended==null,"running game cannot be modified");
        Check(Evaluate(env with{BackupReady=false},rule).Recommended==null,"backup readiness required");
        Check(Evaluate(env with{IntegrityFailed=true},rule).Candidates.Where(x=>!x.External).All(x=>x.State=="BLOCKED"),"hash failures hard-block injection");
        var thirty=rule with{Parameters=new(Passes:30),Validation="experimental",ExperimentalPasses=true};
        Check(Evaluate(env,thirty).Candidates.All(x=>x.Configuration.Passes<9),"30 Pass never generated for recommendation");
        var four=rule with{Parameters=new(Passes:4),Validation="experimental",ExperimentalPasses=true};
        Check(Evaluate(env,four).Candidates.Single(x=>x.Scheme=="author").Automatic==false,"4 Pass rule remains explicit experiment");
        Check(Evaluate(env).Recommended==null&&Evaluate(env).Candidates.All(x=>x.Score<=69),"no evidence cannot get automatic/high-confidence score");
        Check(!RecommendationScoringEngine.CommunityTrusted(new(19,30,40,40,40,40,40,.95,false)),"less than20 devices insufficient");
        Check(!RecommendationScoringEngine.CommunityTrusted(new(30,19,40,40,40,40,40,.95,false)),"accounts deduplicated independently");
        Check(!RecommendationScoringEngine.CommunityTrusted(new(30,30,29,29,29,29,29,.95,false)),"less than30 tests insufficient");
        Check(RecommendationScoringEngine.Wilson(30,30)>.8&&RecommendationScoringEngine.Wilson(3,3)<.8,"Wilson lower bound handles small samples");
        Check(!RecommendationScoringEngine.CommunityTrusted(new(30,30,40,40,40,40,40,.95,true)),"unresolved serious failure blocks community trust");
        var inference=new Dictionary<string,Observation>(env.Fields){["api"]=new("dx12","INFERRED","PE")};
        Check(Evaluate(env with{Fields=inference},rule).Recommended==null,"static import not actual API confirmation");
        var denied=rule with{Id="deny",Deny=true,Description="known broken build"};
        Check(Evaluate(env,rule,denied).Candidates.Where(x=>x.Scheme=="author").All(x=>x.State=="BLOCKED"),"deny overrides otherwise valid rule");
        var fps=Rule("fps");var noInput=new Dictionary<string,Observation>(env.Fields){["upscalerInput"]=new()};
        Check(Evaluate(env with{Fields=noInput},fps).Candidates.Single(x=>x.Scheme=="fps").State=="REQUIRE_ACTION","FPS goal never invents XeFG input");
        Check(Evaluate(env,fps).Candidates.Single(x=>x.Scheme=="fps").Configuration.Ini["XeFG"]["InterpolationCount"]=="1","XeFG remains 2X");
        var fallback=RecommendationFallbackEngine.Diagnose(["ANTI_CHEAT"],result.Recommended,result.Candidates,[rule]);
        Check(fallback.AlternativeIds.Length==0,"anti-cheat never triggers bypass alternatives");
        var fixedRule=daniel with{Fixes=["MODEL_COMPILE_FAILED"]};
        var alt=Evaluate(Environment(gpu:"RX 7900 XT",arch:"rdna3"),fixedRule);
        Check(RecommendationFallbackEngine.Diagnose(["MODEL_COMPILE_FAILED"],null,alt.Candidates,[fixedRule]).AlternativeIds.Length==1,"only explicit verified targeted fix becomes fallback");
        await CacheAndFilesAsync(env,rule);
        Console.WriteLine($"Smart recommendation engine passed: {count} assertions (matching, hard constraints, scoring, authenticated-cache behavior, real file restore).");
    }
    static async Task CacheAndFilesAsync(EnvironmentSnapshot env,SmartCompatibilityRule rule)
    {
        var folder=Path.Combine(Path.GetTempPath(),"mu-recommendation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var key=RandomNumberGenerator.GetBytes(32);
        byte[] Protect(byte[] input,bool encrypt)
        {
            using var aes=new AesGcm(key,16);
            if(encrypt){var nonce=RandomNumberGenerator.GetBytes(12);var output=new byte[input.Length+28];nonce.CopyTo(output,0);aes.Encrypt(nonce,input,output.AsSpan(28),output.AsSpan(12,16));return output;}
            var plain=new byte[input.Length-28];aes.Decrypt(input.AsSpan(0,12),input.AsSpan(28),input.AsSpan(12,16),plain);return plain;
        }
        try
        {
            var cache=new SmartRuleCache(Path.Combine(folder,"cache"),Protect);var doc=new NeuralRuleDocument(1,"r1",Now.AddDays(1),[],[rule]);
            var first=await cache.FetchAsync("owner","https://example.test/","steam:1",()=>Task.FromResult(doc),Now);
            Check(first.Source=="remote","fresh trusted source stored");
            var offline=await cache.FetchAsync("owner","https://example.test/","steam:1",()=>throw new HttpRequestException("offline"),Now);
            Check(offline.Source=="cache"&&offline.Document.Revision=="r1","offline uses valid encrypted owner-bound cache");
            Check(cache.Read("other","https://example.test/","steam:1",Now)==null,"different account cannot read private cache");
            Check(cache.Read("owner","https://example.test/","steam:1",Now.AddDays(2))==null,"expired cache rejected");
            var disabled=doc with{Revision="r2",SmartRules=[rule with{Disabled=true}]};
            await cache.FetchAsync("owner","https://example.test/","steam:1",()=>Task.FromResult(disabled),Now);
            Check(Evaluate(env,cache.Read("owner","https://example.test/","steam:1",Now)!.SmartRules!).Recommended==null,"remote disabling replaces previous cached recommendation");
            var file=Directory.GetFiles(Path.Combine(folder,"cache")).Single();var bytes=File.ReadAllBytes(file);bytes[^1]^=1;File.WriteAllBytes(file,bytes);
            Check(cache.Read("owner","https://example.test/","steam:1",Now)==null,"tampered encrypted cache rejected");
            var invalid=await cache.FetchAsync("owner","https://example.test/","steam:1",()=>throw new System.Text.Json.JsonException("bad schema"),Now);
            Check(invalid.Source=="unavailable"&&invalid.Document.Rules.Length==0,"malformed remote data safely degrades without invented defaults");
            var schemaBad=await cache.FetchAsync("owner","https://example.test/","steam:1",()=>Task.FromResult(doc with{SchemaVersion=99}),Now);
            Check(schemaBad.Source=="unavailable","invalid remote schema safely degrades");
            GameManagement.StorageOverride=Path.Combine(folder,"records");var exe=Path.Combine(folder,"fixture.exe");File.WriteAllText(exe,"fixture");
            GameManagement.Begin(exe,2,"test");File.WriteAllText(Path.Combine(folder,"OptiScaler.ini"),"[User]\nCustom=keep\n");GameManagement.Finish(exe,true);
            var observe=VerificationService.Observe(exe,null,true);
            Check(observe is{Installed:true,Loaded:false,Active:false,ExperienceConfirmed:false,State:"INSTALLED"},"installation or user acceptance never proves ACTIVE");
            GameManagement.Restore(exe);
            Check(GameManagement.Read(exe)?.Phase=="restored"&&!File.Exists(Path.Combine(folder,"OptiScaler.ini")),"existing tracked restore removes introduced configuration");
            var patch=ManagedIni.Set("[XeFG]\nInterpolationCount=1\n[DlssNr]\nEnabled=false\n[User]\nCustom=keep\n","DlssNr","Enabled","true");
            Check(patch.Contains("Custom=keep")&&patch.Contains("InterpolationCount=1"),"section/key patch preserves other settings");
        }
        finally{GameManagement.StorageOverride=null;Directory.Delete(folder,true);}
    }
}
