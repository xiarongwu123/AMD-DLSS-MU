using System.Windows.Forms;
namespace AmdNrAssistant;
public sealed partial class MainForm
{
    async Task InstallOptiScalerStandardAsync(string exe, bool vulkan)
    {
        var progress = new Progress<string>(s => status.Text = s);
        var package = await OptiInstaller.PrepareAsync(exe, progress, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        if (!await RequireFeatureAsync("optiscaler.configure")) return;
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
