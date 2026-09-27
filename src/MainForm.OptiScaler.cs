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
        await Task.Run(() => OptiInstaller.Install(exe, package, vulkan));
        status.Text = "OptiScaler 文件已安装；游戏内按 Insert。运行效果尚未验证。";
        RefreshHome();
        ShowConfigurationComplete(exe, true);
    }
}
