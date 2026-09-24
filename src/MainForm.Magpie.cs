using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel magpiePage = new() { Dock = DockStyle.Fill, Padding = new Padding(28), AutoScroll = true };
    readonly Label magpieStatus = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 16, 0, 12), Text = "首次开启自动下载完整包；后续校验本机组件后直接启动。" };
    readonly ProgressBar magpieProgress = new() { Width = 520, Height = 12, Visible = false, Margin = new Padding(0, 0, 0, 20) };
    RoundedButton? magpieStart;

    void BuildMagpiePage()
    {
        magpiePage.BackColor = Base;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Base, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        body.Controls.Add(Heading("大力喜鹊", "Magpie Experimental", 21));
        var help = Localize(new Label { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = muted, Margin = new Padding(0, 16, 0, 20) },
            "独立窗口缩放工具 · SAOG0721/Magpie " + MagpieIntegration.Tag + "\n\n自动下载、SHA-256 校验、解压并启动，不修改游戏文件。首次默认通用 Lanczos 缩放，不默认开启 DLSS 或补帧。\n\n使用：将游戏设为窗口模式，切回游戏后按 Alt + Shift + A 启用／停止；其他效果在 Magpie 内选择。\n\n实验性 AI 效果取决于显卡与运行组件，不能等同游戏原生 DLSS。请勿叠加多套补帧。",
            "Standalone window scaling · SAOG0721/Magpie " + MagpieIntegration.Tag + "\n\nDownloads, verifies and starts a separate portable installation. Defaults to Lanczos, not DLSS or frame generation.\n\nRun your game in windowed mode, focus it and press Alt + Shift + A to toggle scaling. Choose other effects inside Magpie. AI effects depend on hardware; avoid stacking frame generation.");
        body.Controls.Add(help);
        magpieStart = Action("开启大力喜鹊", "Start Magpie", async (_, _) => await StartMagpieAsync());
        magpieStart.Size = new Size(260, 58); magpieStart.Chamfer = true; magpieStart.BackColor = Acid; magpieStart.ForeColor = OnAccent;
        body.Controls.Add(magpieStart); body.Controls.Add(magpieStatus); body.Controls.Add(magpieProgress);
        body.Controls.Add(Action("项目与使用说明", "Project / help", (_, _) => Process.Start(new ProcessStartInfo(MagpieIntegration.Repository) { UseShellExecute = true })));
        body.SizeChanged += (_, _) => { help.MaximumSize = new Size(Math.Max(200, body.ClientSize.Width - 28), 0); magpieStatus.MaximumSize = help.MaximumSize; magpieProgress.Width = Math.Max(180, Math.Min(520, body.ClientSize.Width - 28)); };
        magpiePage.Controls.Add(body); pageHost.Controls.Add(magpiePage);
    }

    async Task StartMagpieAsync()
    {
        if (busy) { MessageBox.Show(this, "请等待当前操作完成后再开启大力喜鹊。", Text); return; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
        { MessageBox.Show(this, "此集成需要 Windows 10 2004 或更新版本的 x64 系统。", Text); return; }
        // Upstream is single-instance: do not silently switch another installation's settings.
        var processes = Process.GetProcessesByName("Magpie");
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        if (running) { magpieStatus.Text = "Magpie 已在运行。请切回游戏按其快捷键启用效果；如需使用 MU 独立配置，请先从托盘退出已有 Magpie。"; return; }
        busy = true; magpieStart!.Enabled = false; UpdateButtons();
        magpieProgress.Visible = true; magpieProgress.Style = ProgressBarStyle.Marquee;
        magpieStatus.Text = "正在检查大力喜鹊组件…";
        using var session = BeginDownloadSession("大力喜鹊 · Magpie", StartMagpieAsync);
        try
        {
            var phase = new Progress<string>(text => { magpieStatus.Text = text; magpieProgress.Style = ProgressBarStyle.Marquee; });
            var progress = new Progress<int>(value =>
            {
                magpieProgress.Style = value < 100 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                magpieProgress.Value = Math.Clamp(value, 0, 100);
                magpieStatus.Text = value < 100 ? $"正在下载大力喜鹊 · {value}%" : "下载完成，正在校验与准备组件…";
            });
            var exe = await MagpieIntegration.PrepareAsync(progress, phase, lifetime.Token);
            var portableConfig = Path.Combine(Path.GetDirectoryName(exe)!, "config", "v4e", "config.json");
            Core.RejectLinks(portableConfig);
            if (!File.Exists(portableConfig)) throw new IOException("独立配置缺失，已停止启动以免使用其他 Magpie 的全局设置。");
            using var child = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            if (child == null) throw new IOException("未能启动 Magpie。");
            magpieProgress.Style = ProgressBarStyle.Continuous; magpieProgress.Value = 100;
            magpieStatus.Text = "已发起启动。切回窗口模式的游戏，按 Alt + Shift + A 启用效果（不代表效果已生效）。";
        }
        catch (OperationCanceledException) { magpieProgress.Visible = false; magpieStatus.Text = "下载已取消，可重新开启。"; }
        catch (Exception e) { magpieProgress.Visible = false; magpieStatus.Text = "开启失败：" + e.Message; }
        finally { busy = false; magpieStart.Enabled = true; libraryDownloadProgress.Visible = false; UpdateButtons(); }
    }
}
