using System.Windows.Forms;
using Mu.Compatibility;
namespace AmdNrAssistant;
public sealed partial class MainForm
{
    NeuralOptions neuralOptions = new();
    Task<CompatibilityGpu?>? neuralGpuRead;
    DateTime neuralGpuReadAt;
    async Task<NeuralPlan> BuildNeuralPlanAsync(string exe, NeuralOptions options, bool refreshRules = false, CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? lifetime.Token;
        if (neuralGpuRead == null || neuralGpuRead.IsFaulted || neuralGpuReadAt < DateTime.UtcNow.AddMinutes(-5))
        {
            neuralGpuReadAt = DateTime.UtcNow;
            async Task<CompatibilityGpu?> Read() => SmartRenderAdvisor.SelectGpu(await new CompatibilityEnvironmentReader().ReadGpusAsync(lifetime.Token));
            neuralGpuRead = Read();
        }
        var gpu = await neuralGpuRead.WaitAsync(token);
        var api = "unknown";
        try { api = await Task.Run(() => NeuralRenderPlanner.DetectApi(exe), token); }
        catch (Exception e) when (e is IOException or BadImageFormatException) { }
        var gameId = ReactPresetGameKey(exe);
        // Paid game presets never travel through the anonymous endpoint or disk cache.
        // A generic scheme may still use conservative defaults when no preset was requested.
        Mu.Neural.NeuralRuleDocument? rules = null;
        if (MembershipAccess.CanUse(accountClient.Account, "schemes.game.view", DateTimeOffset.UtcNow))
        {
            try { rules = Mu.Neural.NeuralRules.Parse((await accountClient.GetNeuralRulesAsync(gameId, token)).GetRawText()); }
            catch (AccountApiException e) when (e.Status == System.Net.HttpStatusCode.NotFound)
            { /* Manual schemes can use their unverified defaults without remote presets. */ }
        }
        return NeuralRenderPlanner.Select(gameId, gpu?.Name ?? "", api, rules, options, DateTimeOffset.UtcNow);
    }
    async Task InstallNeuralRenderAsync(string exe)
    {
        status.Text = "正在匹配游戏兼容规则与显卡…";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, DownloadSources.CurrentSession.Value?.Token ?? CancellationToken.None);
        var token = operation.Token;
        var plan = await BuildNeuralPlanAsync(exe, neuralOptions, true, token);
        if(smartInstallingCandidate is {} selected)
        {
            if(plan.Backend!=selected.Configuration.Backend||plan.Proxy!=selected.Configuration.Proxy||plan.Passes!=selected.Configuration.Passes)throw new IOException("实际安装参数与推荐计划不一致，已停止。");
            plan=plan with{RuleRevision=smartInstallingRevision,Status=selected.Evidence};
        }
        if (plan.Backend != "mochizuki")
        {
            if (!await EnsureHip72ForRx6000Async(exe)) return;
            if (!SmartRenderDiagnostics.HasHipRuntime()) throw new MissingAmdHipRuntimeException();
        }
        var progress = new Progress<string>(s => status.Text = s);
        if (plan.Backend == "daniel") await Task.Run(NeuralRenderInstaller.EnsureBundledDll, token);
        var missing = await Task.Run(() => NeuralRenderInstaller.MissingResources(exe, plan), token);
        if (missing.Length > 0)
            throw new IOException(string.Join("\n", missing) + "\n请打开高级设置中的“准备本机资源”。");
        var package = await NeuralRenderInstaller.PrepareAsync(progress, token);
        try
        {
            if (plan.Backend == "mochizuki") await NeuralRenderInstaller.PrepareModelAsync(package, progress, token);
            token.ThrowIfCancellationRequested();
            if (!await RequireFeatureAsync("schemes.premium.configure")) return;
            await ValidateSmartBeforeWriteAsync(exe);
            using var transaction = BeginAccountTransaction();
            status.Text = "正在备份游戏文件并部署神经渲染…";
            await Task.Run(() => NeuralRenderInstaller.Install(exe, package, plan, token, progress));
            RefreshHome(); ShowConfigurationComplete(exe, true);
        }
        finally
        {
            try { if (Directory.Exists(package)) Directory.Delete(package, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Diagnostics.Record(exe, "neural-cache", "cleanup-failed", e.Message); }
        }
    }
    string x86GraphicsApi="auto";
    async Task InstallLegacyBridgeAsync(string exe)
    {
        var api = x86GraphicsApi == "auto" ? X86Bridge.DetectApi(exe) : x86GraphicsApi;
        _ = LegacyBridge.Proxy(api);
        if (!await EnsureHip72ForRx6000Async(exe)) return;
        var package = await LegacyBridge.PrepareAsync(new Progress<string>(s => status.Text = s), lifetime.Token);
        if (!await RequireFeatureAsync("schemes.premium.configure")) return;
        await ValidateSmartBeforeWriteAsync(exe);
        using var transaction = BeginAccountTransaction();
        await Task.Run(() => LegacyBridge.Install(exe, package, api));
        status.Text = "老游戏桥接已配置；Home 打开 ReShade，效果需进入游戏验证。";
        RefreshHome(); ShowConfigurationComplete(exe, true);
    }
    async Task InstallX86BridgeAsync(string exe)
    {
        var api=x86GraphicsApi=="auto" ? X86Bridge.DetectApi(exe) : x86GraphicsApi;
        _=X86Bridge.Proxy(api); // refuse an unknown renderer before downloading or modifying the game
        var package=await X86Bridge.PrepareAsync(new Progress<string>(s=>status.Text=s),lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        if(!await RequireFeatureAsync("schemes.premium.configure"))return;
        await ValidateSmartBeforeWriteAsync(exe);
        using var transaction=BeginAccountTransaction();
        await Task.Run(()=>X86Bridge.Install(exe,package,api));
        status.Text="32位桥接已配置；游戏内 Home 打开面板，Ctrl+End 切换神经渲染。效果尚未验证。";
        RefreshHome();ShowConfigurationComplete(exe,true);
    }
    async Task InstallOptiScalerStandardAsync(string exe, bool vulkan)
    {
        var progress = new Progress<string>(s => status.Text = s);
        var package = await OptiInstaller.PrepareAsync(exe, progress, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        if (!await RequireFeatureAsync("optiscaler.configure")) return;
        await ValidateSmartBeforeWriteAsync(exe);
        using var transaction = BeginAccountTransaction();
        status.Text = "正在写入组件并建立恢复记录…";
        await Task.Run(() => OptiInstaller.Install(exe, package, vulkan, optiFgInput));
        status.Text = "OptiScaler 文件已安装；游戏内按 Insert。运行效果尚未验证。";
        RefreshHome();
        ShowConfigurationComplete(exe, true);
    }

    async Task InstallCombinedRenderAsync(string exe)
    {
        var progress = new Progress<string>(s => status.Text = s);
        var package = await CombinedRenderInstaller.PrepareAsync(exe, progress, lifetime.Token);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (!await RequireFeatureAsync("dlss.configure")) return;
            await ValidateSmartBeforeWriteAsync(exe);
            using var transaction = BeginAccountTransaction();
            status.Text = "正在部署 DLSS 5 与 XeFG，并建立恢复记录…";
            await Task.Run(() => CombinedRenderInstaller.Install(exe, package));
            status.Text = "组件已部署；请在游戏中按 Insert 检查 DLSS NR 与帧生成是否生效。";
            RefreshHome();
            ShowConfigurationComplete(exe, true);
        }
        finally
        {
            try { if (Directory.Exists(package)) Directory.Delete(package, true); }
            catch (IOException e) { Diagnostics.Record(exe, "combined-cache", "cleanup-failed", e.Message); }
            catch (UnauthorizedAccessException e) { Diagnostics.Record(exe, "combined-cache", "cleanup-failed", e.Message); }
        }
    }
}
