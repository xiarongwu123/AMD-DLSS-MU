using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel magpiePage = new() { Dock = DockStyle.Fill, Padding = new Padding(28), AutoScroll = true };
    readonly Label magpieStatus = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 16, 0, 12), Text = "首次开启自动下载完整包；后续校验本机组件后直接启动。" };
    readonly ProgressBar magpieProgress = new() { Width = 520, Height = 12, Visible = false, Margin = new Padding(0, 0, 0, 20) };
    readonly FlowLayoutPanel magpieGameCards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Margin = Padding.Empty };
    RoundedButton? magpieStart;

    void BuildMagpiePage()
    {
        magpiePage.BackColor = Base;
        var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, BackColor = Base, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 345));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        var banner = new ArtworkPanel("AmdNrAssistant.mu-hero-art.png") { Dock = DockStyle.Fill, Radius = 24, BackColor = Color.FromArgb(5, 27, 28), BorderColor = Color.FromArgb(27, 111, 65), Margin = new Padding(0, 0, 0, 18) };
        banner.Controls.Add(new Label { Text = "大力喜鹊", Font = new Font(Font.FontFamily, 29, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent, Location = new Point(30, 26), Size = new Size(430, 64) });
        banner.Controls.Add(new Label { Text = "独立窗口缩放工具 · SAOG0721/Magpie", Font = new Font(Font.FontFamily, 13), ForeColor = Muted, BackColor = Color.Transparent, Location = new Point(32, 98), Size = new Size(560, 42) });
        body.Controls.Add(banner, 0, 0); body.SetColumnSpan(banner, 2);
        var main = new RoundedPanel { Dock = DockStyle.Fill, Radius = 22, BackColor = Surface, BorderColor = Line, Padding = new Padding(28), Margin = new Padding(0, 0, 18, 16) };
        var startContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        startContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); startContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        startContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); startContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 118)); startContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        startContent.Controls.Add(new Label { Text = "⚡  一键开启大力喜鹊", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 23, FontStyle.Bold), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        startContent.SetColumnSpan(startContent.Controls[0], 2);
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        magpieStart = Action("开启大力喜鹊", "Start Magpie", async (_, _) => await StartMagpieAsync());
        magpieStart.Size = new Size(350, 62); magpieStart.Radius = 20; magpieStart.BackColor = Acid; magpieStart.ForeColor = OnAccent;
        magpieStart.Font = new Font(Font.FontFamily, 14, FontStyle.Bold); magpieStart.LightningEffect = true;
        left.Controls.Add(magpieStart); left.Controls.Add(magpieStatus); left.Controls.Add(magpieProgress);
        startContent.Controls.Add(left, 0, 1); startContent.SetRowSpan(left, 2);
        var features = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        features.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); features.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        string[] featureTexts = { "◉ 自动下载依赖", "✓ 校验组件", "▣ 独立窗口运行", "↶ 不改游戏文件" };
        for (var i = 0; i < featureTexts.Length; i++)
        {
            var feature = new RoundedPanel { Dock = DockStyle.Fill, Radius = 16, BackColor = SelectedSurface, BorderColor = Line, Margin = new Padding(4), Padding = new Padding(8) };
            feature.Controls.Add(new Label { Text = featureTexts[i], Dock = DockStyle.Fill, ForeColor = Ink, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter });
            features.Controls.Add(feature, i % 2, i / 2);
        }
        startContent.Controls.Add(features, 1, 1); startContent.SetRowSpan(features, 2);
        main.Controls.Add(startContent); body.Controls.Add(main, 0, 1);
        var instructions = new RoundedPanel { Dock = DockStyle.Fill, Radius = 22, BackColor = Surface, BorderColor = Line, Padding = new Padding(22), Margin = new Padding(0, 0, 0, 16) };
        var instructionsText = new Label { Dock = DockStyle.Fill, ForeColor = Ink, Font = new Font(Font.FontFamily, 10.5f), Text = "使用说明\n\n1  开启后自动准备并启动 Magpie\n\n2  把游戏设为窗口模式\n\n3  切回游戏按 Alt + Shift + A\n\n默认是 Lanczos 缩放；不自动启用 DLSS 或补帧。", AutoEllipsis = false };
        instructions.Controls.Add(instructionsText); body.Controls.Add(instructions, 1, 1);
        var recent = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Surface, BorderColor = Line, Padding = new Padding(16), Margin = new Padding(0, 0, 18, 0) };
        recent.Controls.Add(magpieGameCards);
        recent.Controls.Add(new Label { Text = "最近的游戏", Dock = DockStyle.Top, Height = 38, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = Ink });
        body.Controls.Add(recent, 0, 2);
        var notes = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Surface, BorderColor = Line, Padding = new Padding(22) };
        notes.Controls.Add(new Label { Text = "注意事项\n\n• 实验性 AI 效果取决于显卡和运行组件\n• 与游戏原生 DLSS 不同\n• 不建议叠加多套补帧", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font(Font.FontFamily, 10.5f) });
        body.Controls.Add(notes, 1, 2);
        magpiePage.Controls.Add(body); pageHost.Controls.Add(magpiePage);
        RenderMagpieGames();
    }

    void RenderMagpieGames()
    {
        if (selectedCard?.Parent == magpieGameCards) selectedCard = null;
        while (magpieGameCards.Controls.Count > 0) { var c = magpieGameCards.Controls[0]; magpieGameCards.Controls.Remove(c); c.Dispose(); }
        foreach (var game in libraryGames.Take(3))
        {
            var card = CreateGameCard(game);
            SizeHomeCard(card, 175, 192);
            magpieGameCards.Controls.Add(card);
        }
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
