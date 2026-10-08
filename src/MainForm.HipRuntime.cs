using System.Diagnostics;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task<bool> EnsureHip72ForRx6000Async(string gameExe)
    {
        if (!GameManagement.HasRx6000(GameManagement.Hardware())) return true;
        if (HipRuntime.HasVersion72()) return true;

        var choice = MessageBox.Show(this,
            "检测到 RX 6000，但未确认本机已安装 AMD HIP 7.2。\n\n" +
            "MU 将打开 AMD 官方许可与下载页。接受许可并下载后，请在下一窗口选择安装包；MU 会验证 AMD 数字签名并启动安装程序。\n\n" +
            "AMD 完整安装程序包含显卡驱动，可能要求管理员权限和重启；AMD 官方 7.2 支持列表未列出 RX 6000，安装器可能拒绝该显卡。\n\n继续？",
            "安装 AMD HIP 7.2", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (choice != DialogResult.Yes) return false;

        Process.Start(new ProcessStartInfo(HipRuntime.DownloadPage) { UseShellExecute = true });
        using var picker = new OpenFileDialog
        {
            Title = "下载完成后选择 AMD HIP 7.2 安装包",
            Filter = "AMD HIP 7.2 安装程序 (*.exe)|*.exe",
            CheckFileExists = true,
            FileName = HipRuntime.InstallerName,
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return false;

        status.Text = "正在验证 AMD 安装包签名…";
        await HipRuntime.VerifyAmdInstallerAsync(picker.FileName);
        status.Text = "正在启动 AMD HIP 7.2 安装程序…";
        using var process = Process.Start(HipRuntime.CreateInstallStartInfo(picker.FileName))
            ?? throw new IOException("AMD 安装程序未启动。");
        await process.WaitForExitAsync();
        Diagnostics.Record(gameExe, "hip-7.2", process.ExitCode == 0 ? "installer-closed" : "installer-failed", "AMD installer exit code " + process.ExitCode);
        if (process.ExitCode != 0)
            throw new IOException("AMD HIP 7.2 安装程序未正常完成，退出码：" + process.ExitCode);
        MessageBox.Show(this,
            "AMD 安装程序已关闭。请按安装程序提示重启电脑；重启后再点击“一键配置”，MU 会重新检查 HIP 7.2。",
            "AMD HIP 7.2", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }
}
