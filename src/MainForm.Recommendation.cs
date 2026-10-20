using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Mu.Neural;
using Mu.Recommendation;
using Mu.Telemetry;
namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Dictionary<string,RecommendationResult> smartPlans=new();
    readonly Dictionary<string,RecommendationResult> smartLastResults=new();
    readonly Dictionary<string,SmartCompatibilityRule[]> smartLastRules=new();
    RecommendationCandidate? smartInstallingCandidate;
    string smartInstallingRevision="";
    string? smartPreflightExe;
    string? smartPreflightBackend;
    bool smartApplying;
    bool smartExecutingInstall;
    sealed record SmartReceipt(RecommendationCandidate Candidate,string Revision,DateTimeOffset InstalledAt,Dictionary<string,long> LogOffsets,string[]? FailureCodes=null);
    string SmartGameKey(string exe)=>ReactPresetGameKey(exe).ToLowerInvariant();
    string SmartReceiptPath(string exe)=>Path.Combine(GameManagement.State(exe),"recommendation-receipt.json");
    SmartReceipt? ReadSmartReceipt(string exe)
    {
        var path=SmartReceiptPath(exe);Core.RejectLinks(path);
        if(!File.Exists(path)||new FileInfo(path).Length>128*1024)return null;
        return JsonSerializer.Deserialize<SmartReceipt>(File.ReadAllText(path),NeuralRules.Json);
    }
    void SaveSmartReceipt(string exe,RecommendationCandidate candidate,string revision)
    {
        var offsets=new Dictionary<string,long>();foreach(var name in new[]{"OptiScaler.log","dlssnr_on_amd.log","dlss5-neural.log"})
        {var file=Path.Combine(Path.GetDirectoryName(exe)!,name);Core.RejectLinks(file);offsets[name]=File.Exists(file)?new FileInfo(file).Length:0;}
        var path=SmartReceiptPath(exe);Core.RejectLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        NeuralRenderInstaller.AtomicWrite(path,JsonSerializer.SerializeToUtf8Bytes(new SmartReceipt(candidate,revision,DateTimeOffset.UtcNow,offsets),NeuralRules.Json));
    }
    async Task<EnvironmentSnapshot> SmartEnvironmentAsync(string exe,JsonElement args)
    {
        var screen=Screen.FromControl(this).Bounds;
        return await EnvironmentDetector.DetectAsync(exe,SmartGameKey(exe),ReactPresetGameName(exe),screen.Width,screen.Height,lifetime.Token,
            Arg(args,"confirmedApi"),Arg(args,"confirmedInput"),Arg(args,"windowed"));
    }
    bool SmartMayRead=>accountClient.CanKeepVerifiedView&&MembershipAccess.CanUse(accountClient.Account,"schemes.game.view",DateTimeOffset.UtcNow);
    async Task<(NeuralRuleDocument Document,string Source)> SmartRulesAsync(string game)
    {
        if(!SmartMayRead)throw new IOException("游戏专属推荐需要有效的高级会员。");
        var owner=accountClient.Account!.Id;
        var cache=new SmartRuleCache(Path.Combine(Core.InstallerCacheDirectory,"smart-rules"),AccountTokenStore.Transform);
        return await cache.FetchAsync(owner,AccountApiClient.DefaultBaseUrl,game,async()=>
        {
            try{return NeuralRules.Parse((await accountClient.GetSmartRulesAsync(game,lifetime.Token)).GetRawText());}
            catch(AccountApiException e)when((int)e.Status>=500){throw new HttpRequestException("规则服务暂时不可用。",e);}
        },DateTimeOffset.UtcNow);
    }
    async Task<RecommendationResult> BuildSmartRecommendationAsync(JsonElement args,bool remember=true)
    {
        var exe=ReactGame(args);var env=await SmartEnvironmentAsync(exe,args);var rules=await SmartRulesAsync(env.GameId);
        var scope=Arg(args,"scope");if(scope.Length>0&&!RecommendationRuleSchema.Schemes.Contains(scope))throw new IOException("未知方案范围。");
        var result=RecommendationService.Evaluate(env,rules.Document.SmartRules??[],rules.Document.Weights??new(),rules.Document.Revision,rules.Source,
            rules.Document.ExpiresUtc<DateTimeOffset.UtcNow.AddMinutes(15)?rules.Document.ExpiresUtc:DateTimeOffset.UtcNow.AddMinutes(15),
            Arg(args,"goal","stability"),DateTimeOffset.UtcNow,scope.Length==0?null:scope);
        if(remember){smartLastResults[env.GameId]=result;smartLastRules[env.GameId]=rules.Document.SmartRules??[];foreach(var old in smartPlans.Where(p=>p.Value.ExpiresUtc<=DateTimeOffset.UtcNow).Select(p=>p.Key).ToArray())smartPlans.Remove(old);if(smartPlans.Count>=32)smartPlans.Remove(smartPlans.Keys.First());smartPlans[result.PlanId]=result;}
        return result;
    }
    async Task ManualSmartPreflightAsync(string exe,string scheme,JsonElement args)
    {
        if(scheme=="base")return; // Opening an external manager does not deploy to the game.
        var env=await SmartEnvironmentAsync(exe,args);
        var backend=scheme=="author"?"mochizuki":scheme=="standard"?"none":scheme is "compat" or "online"?ReadNeuralOptions(args).Backend:"daniel";
        if(backend=="auto")backend="daniel"; // No evidence does not justify a silent alternate backend.
        SmartCompatibilityRule[] rules=[];
        if(SmartMayRead)
        {
            try { rules=(await SmartRulesAsync(env.GameId)).Document.SmartRules??[]; }
            catch(AccountApiException e) when(e.Status==System.Net.HttpStatusCode.NotFound)
            { /* Missing optional rules on an old server must not lock a manual software scheme. */ }
        }
        var matching=rules.Where(r=>r.Scheme==scheme&&r.Backend==backend&&CompatibilityRuleEngine.Match(r,env,DateTimeOffset.UtcNow))
            .OrderByDescending(r=>CompatibilityRuleEngine.Priority(r,env)).FirstOrDefault();
        var cap=CapabilityCatalog.For(scheme,Core.IsX86(exe)?32:64);
        if(Number(args,"mode",-1)==0){cap=new("base","原有Daniel模式","0.6.0",false,false,["dx12"],[64]);matching=null;}
        var errors=HardConstraintEvaluator.Evaluate(env,cap,backend,matching,rules,DateTimeOffset.UtcNow,true);
        if(scheme is "author" or "compat" or "online")
        {
            var options=ReadNeuralOptions(args);
            if((options.NrInline==true||options.EarlyAnyModuleWrap==true||options.AllowEnhancedBarriers==true)&&
                (matching==null||matching.Validation is not ("verified" or "community")||
                    options.NrInline==true&&!matching.Parameters.NrInline||options.EarlyAnyModuleWrap==true&&!matching.Parameters.EarlyAnyModuleWrap||
                    options.AllowEnhancedBarriers==true&&!matching.Parameters.AllowEnhancedBarriers))throw new IOException("特殊兼容参数缺少明确的有效验证规则，不能盲目启用。");
        }
        var hard=errors.Where(e=>e.State is "BLOCKED" or "REQUIRE_ACTION").ToArray();
        if(hard.Length>0)throw new IOException(string.Join("\n",hard.Select(e=>$"[{e.Code}] {e.Message}")));
        if(smartInstallingCandidate==null&&errors.Any(e=>e.State=="UNVERIFIED")&&await UiMessageAsync(
            string.Join("\n",errors.Select(e=>e.Message))+"\n此方案缺少验证，是否仍主动尝试？","未验证配置",MessageBoxButtons.YesNo,"确认尝试",MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
            throw new IOException("已取消未验证配置，游戏文件未改动。");
        smartPreflightExe=exe;smartPreflightBackend=backend;
    }
    async Task ValidateExistingSmartSettingsAsync(string exe,NeuralOptions options)
    {
        var record=GameManagement.Read(exe);
        if(record?.Neural==null)return;
        var env=await SmartEnvironmentAsync(exe,JsonSerializer.SerializeToElement(new{}));
        var backend=options.Backend=="auto"?record.Neural.Backend:options.Backend;
        var rules=SmartMayRead?(await SmartRulesAsync(env.GameId)).Document.SmartRules??[]:Array.Empty<SmartCompatibilityRule>();
        var rule=rules.Where(r=>r.Backend==backend&&CompatibilityRuleEngine.Match(r,env,DateTimeOffset.UtcNow)).OrderByDescending(r=>CompatibilityRuleEngine.Priority(r,env)).FirstOrDefault();
        var issues=HardConstraintEvaluator.Evaluate(env,CapabilityCatalog.For(backend=="mochizuki"?"author":"compat"),backend,rule,rules,DateTimeOffset.UtcNow)
            .Where(i=>i.State=="BLOCKED"||i.Code is "GAME_RUNNING" or "MISSING_RESOURCE").ToArray();
        if(issues.Length>0)throw new IOException(string.Join("\n",issues.Select(i=>i.Message)));
        if((options.NrInline==true||options.EarlyAnyModuleWrap==true||options.AllowEnhancedBarriers==true)&&
            (rule==null||rule.Validation is not ("verified" or "community")||options.NrInline==true&&!rule.Parameters.NrInline||
                options.EarlyAnyModuleWrap==true&&!rule.Parameters.EarlyAnyModuleWrap||options.AllowEnhancedBarriers==true&&!rule.Parameters.AllowEnhancedBarriers))
            throw new IOException("当前设备缺少这些特殊参数的有效兼容规则。");
    }
    async Task ValidateSmartBeforeWriteAsync(string exe)
    {
        if(smartInstallingCandidate is not {} candidate)return;
        var now=DateTimeOffset.UtcNow;
        var rules=await SmartRulesAsync(SmartGameKey(exe));
        if(rules.Source!="remote"||rules.Document.Revision!=smartInstallingRevision)throw new IOException("下载期间规则变化或不可验证，已停止写入，请重新检测。");
        var config=candidate.Configuration;
        var env=await SmartEnvironmentAsync(exe,JsonSerializer.SerializeToElement(new{confirmedApi=config.GraphicsApi,
            confirmedInput=smartLastResults.GetValueOrDefault(SmartGameKey(exe))?.Environment.Field("upscalerInput").Value??"",
            windowed=smartLastResults.GetValueOrDefault(SmartGameKey(exe))?.Environment.Field("windowed").Value??""}));
        var rule=(rules.Document.SmartRules??[]).SingleOrDefault(r=>r.Id==candidate.RuleId&&r.Version==candidate.RuleVersion&&CompatibilityRuleEngine.Match(r,env,now));
        if(candidate.RuleId!=null&&rule==null)throw new IOException("此推荐规则已禁用、过期或不再匹配。");
        var issues=HardConstraintEvaluator.Evaluate(env,CapabilityCatalog.For(candidate.Scheme,config.Bitness),config.Backend,rule,rules.Document.SmartRules??[],now);
        if(issues.Any(i=>i.State is "BLOCKED" or "REQUIRE_ACTION"))throw new IOException(string.Join("\n",issues.Select(i=>i.Message)));
        var before=smartLastResults.GetValueOrDefault(SmartGameKey(exe))?.Environment;
        var normalized=env;
        if(before is not null&&before.Resources.GetValueOrDefault("hip-preparable")&&!before.Resources.GetValueOrDefault("hip")&&env.Resources.GetValueOrDefault("hip"))
        {var resources=new Dictionary<string,bool>(env.Resources){["hip"]=false};normalized=env with{Resources=resources};}
        if(EnvironmentDetector.Fingerprint(normalized)!=candidate.Fingerprint)throw new IOException("下载期间游戏或组件发生变化，已停止写入，请重新检测。");
    }
    async Task<object?> ApplySmartRecommendationAsync(JsonElement args)
    {
        if(smartApplying||reactMutation||busy)throw new IOException("请等待当前配置操作完成。");
        smartApplying=true;PublishReactState(true);
        try{return await ApplySmartRecommendationCoreAsync(args);}
        finally{smartApplying=false;PublishReactState(true);}
    }
    async Task<object?> ExecuteSmartCommandAsync(string command,JsonElement args)
    {
        smartExecutingInstall=true;
        try{return await ReactCommandAsync(command,args,[]);}
        finally{smartExecutingInstall=false;}
    }
    async Task<object?> ApplySmartRecommendationCoreAsync(JsonElement args)
    {
        var planId=Arg(args,"planId");var exe=ReactGame(args);
        if(!smartPlans.TryGetValue(planId,out var old)||old.GameId!=SmartGameKey(exe)||old.ExpiresUtc<=DateTimeOffset.UtcNow)throw new IOException("推荐计划已失效，请重新检测。");
        var selected=old.Candidates.SingleOrDefault(c=>c.Id==Arg(args,"candidateId"))??throw new IOException("候选不属于此计划。");
        await ReactAuthorize("schemes.game.view");if(selected.Premium)await ReactAuthorize("schemes.premium.configure");
        var current=await BuildSmartRecommendationAsync(args,false);
        var candidate=current.Candidates.SingleOrDefault(c=>c.Id==selected.Id);
        if(current.RuleSource!="remote"||candidate==null||current.RuleRevision!=old.RuleRevision||current.Environment.Fingerprint!=old.Environment.Fingerprint||
            JsonSerializer.Serialize(candidate.Configuration,NeuralRules.Json)!=JsonSerializer.Serialize(selected.Configuration,NeuralRules.Json))
            throw new IOException("环境、规则或参数已变化，或无法重新验证规则，请重新检测后配置。");
        if(candidate.State is "BLOCKED" or "REQUIRE_ACTION")throw new IOException(string.Join("\n",candidate.Issues.Select(x=>x.Message)));
        if(!candidate.Automatic)
        {
            if(!Flag(args,"confirmExperimental"))throw new IOException("此候选未经完整验证，请主动确认实验风险。");
            if(await UiMessageAsync("此配置缺少完整稳定性证据，可能失败或影响画面。MU保留恢复记录，是否继续？","实验候选确认",MessageBoxButtons.YesNo,"确认尝试",MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return null;
        }
        var config=candidate.Configuration;var p=config.Parameters;
        object? result;
        SaveSmartReceipt(exe,candidate,current.RuleRevision);
        smartInstallingCandidate=candidate;smartInstallingRevision=current.RuleRevision;
        try {
        if(candidate.Scheme is "base" or "amd-magpie")
            {
                result=await ExecuteSmartCommandAsync(candidate.Scheme=="base"?"daniel08":"amdMagpie",JsonSerializer.SerializeToElement(new{gameId=Arg(args,"gameId"),
                    confirmedApi=Arg(args,"confirmedApi"),confirmedInput=Arg(args,"confirmedInput"),windowed=Arg(args,"windowed")}));
                if(result is not true)throw new IOException("外部工具未启动，未记录为配置成功。");
            }
        else
        {
            var mode=candidate.Scheme switch{"fps"=>4,"legacy"=>config.Bitness==32?5:8,"standard"=>2,_=>6};
            var installArgs=JsonSerializer.SerializeToElement(new{gameId=Arg(args,"gameId"),mode,scheme=candidate.Scheme=="standard"?"":candidate.Scheme,
                confirmedApi=Arg(args,"confirmedApi"),confirmedInput=Arg(args,"confirmedInput"),windowed=Arg(args,"windowed"),
                x86Api=config.GraphicsApi,vulkan=config.GraphicsApi=="vulkan",fgInput="nofg",
                neural=new{backend=config.Backend,graphicsApi=config.GraphicsApi,proxy=config.Proxy,passes=p.Passes,modelScale=p.ModelScale,temporal=p.Temporal,
                    historyStrength=p.HistoryStrength,applyModel=p.ApplyModel,dynamicResolution=p.DynamicResolution,nrInline=p.NrInline,
                    earlyAnyModuleWrap=p.EarlyAnyModuleWrap,allowEnhancedBarriers=p.AllowEnhancedBarriers}});
            result=await ExecuteSmartCommandAsync("configure",installArgs);
            if(GameManagement.Read(exe) is not {Phase:"installed"})throw new IOException("未取得安装记录，不记录为INSTALLED。");
        }
        SaveSmartReceipt(exe,candidate,current.RuleRevision);smartPlans.Remove(planId);
        } catch {
            var receipt=ReadSmartReceipt(exe);
            if(receipt!=null)NeuralRenderInstaller.AtomicWrite(SmartReceiptPath(exe),JsonSerializer.SerializeToUtf8Bytes(receipt with{FailureCodes=["INSTALL_FAILED"]},NeuralRules.Json));
            throw;
        } finally {smartInstallingCandidate=null;smartInstallingRevision="";smartPreflightExe=null;}

        return new{external=candidate.External,verification=VerificationService.Observe(exe,candidate),result};
    }
    (VerificationResult Result,FallbackAdvice Advice) VerifySmart(string exe,string feedback)
    {
        var receipt=ReadSmartReceipt(exe);var report=VerificationService.Observe(exe,receipt?.Candidate,feedback=="acceptable");
        var failures=new List<string>(report.FailureCodes.Concat(receipt?.FailureCodes??[]));
        if(receipt!=null)foreach(var pair in receipt.LogOffsets)
        {
            var file=Path.Combine(Path.GetDirectoryName(exe)!,pair.Key);Core.RejectLinks(file);if(!File.Exists(file))continue;
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            // Truncation starts a new log. Old bytes predating the recorded install are excluded.
            stream.Position=stream.Length<pair.Value?0:Math.Max(pair.Value,stream.Length-128*1024);
            using var reader=new StreamReader(stream);failures.AddRange(VerificationService.ClassifyFreshLog(reader.ReadToEnd()));
        }
        if(feedback is "image_abnormal" or "crashed")failures.Add(feedback=="crashed"?"GAME_CRASH":"IMAGE_ABNORMAL");
        report=report with{State=failures.Count>0?"FAILED":report.State,FailureCodes=failures.Distinct().ToArray()};
        var gameId=SmartGameKey(exe);
        var previous=smartLastResults.GetValueOrDefault(gameId);
        var advice=RecommendationFallbackEngine.Diagnose(report.FailureCodes,receipt?.Candidate,
            previous?.ExpiresUtc>DateTimeOffset.UtcNow?previous.Candidates:[],smartLastRules.GetValueOrDefault(gameId)??[]);
        return(report,advice);
    }
    async Task SubmitSmartFeedbackAsync(string exe,JsonElement args)
    {
        if(!Flag(args,"consent"))throw new IOException("需要用户主动同意才上传匿名验证记录。");
        var receipt=ReadSmartReceipt(exe)??throw new IOException("没有与此游戏关联的推荐安装记录。");
        var feedback=Arg(args,"feedback","unknown");if(feedback is not ("unknown" or "acceptable" or "image_abnormal" or "crashed"))throw new IOException("未知反馈。");
        var verified=VerifySmart(exe,feedback).Result;var env=await SmartEnvironmentAsync(exe,args);
        var machine=Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography","MachineGuid",null) as string;
        if(string.IsNullOrWhiteSpace(machine))throw new IOException("无法生成匿名设备标识，未上传。");
        var device=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("MU-Recommendation-v1\n"+machine)));
        var c=receipt.Candidate.Configuration;var at=DateTimeOffset.UtcNow;
        var observation=new RecommendationObservation(device,env.GameId,env.Field("gameBuild").Value,env.Field("gpuModel").Value,env.Field("architecture").Value,
            env.Field("driver").Value,c.GraphicsApi,c.Bitness,c.Scheme,c.Backend,c.ComponentVersion,receipt.Candidate.Id,receipt.Revision,
            verified.Installed,verified.Loaded,verified.Active,verified.ExperienceConfirmed,feedback=="crashed"?true:null,verified.FailureCodes,feedback,at,env.Field("gameVersion").Value,env.Field("deviceId").Value);
        var upload=new GameTelemetryUpload(Guid.NewGuid().ToString(),env.GameId,null,null,null,null,null,null,null,null,"recommendation",AutoUpdate.DisplayVersion,
            at,at,null,0,null,null,null,null,null,"recommendation-observation",observation);
        await accountClient.SubmitTelemetryAsync(upload,lifetime.Token);
    }
}
