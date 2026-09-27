using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel magpiePage = new AmbientCanvasPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16), AutoScroll = true };
    readonly Label magpieStatus = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 16, 0, 12), Text = "首次开启自动下载完整包；后续校验本机组件后直接启动。" };
    readonly AccentProgressBar magpieProgress = new() { Width = 520, Height = 12, Visible = false, Margin = new Padding(0, 0, 0, 20) };
    readonly FlowLayoutPanel magpieGameCards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Margin = Padding.Empty };
    readonly FlowLayoutPanel magpieConfiguredCards = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Margin = Padding.Empty };
    RoundedButton? magpieStart;

    void BuildMagpiePage()
    {
        magpiePage.BackColor = Base;
        var body = new TableLayoutPanel { Dock = DockStyle.Top, Height = 1000, ColumnCount = 2, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 285));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        var banner = new ArtworkPanel("AmdNrAssistant.mu-hero-art.png") { Dock = DockStyle.Fill, Radius = 18, ShowBorder = true,
            FadeBottom = true, BackColor = Base, Margin = new Padding(0, 0, 0, 6), FocusX = .68f };
        banner.Controls.Add(new Label { Text = "大力喜鹊", Font = new Font(Font.FontFamily, 31, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent, Location = new Point(22, 12), Size = new Size(520, 62) });
        var recommendation = new RoundedPanel { Location = new Point(254, 22), Size = new Size(76, 31), Radius = 12,
            BackColor = SelectedSurface, BorderColor = Line };
        recommendation.Controls.Add(new Label { Text = "推荐", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Acid, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), BackColor = Color.Transparent });
        banner.Controls.Add(recommendation);
        banner.Controls.Add(new Label { Text = "一键开启 Magpie · 释放 AMD 显卡的窗口缩放潜力", Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent, Location = new Point(24, 73), Size = new Size(800, 36) });
        banner.Controls.Add(new Label { Text = "基于 SAOG0721/Magpie · 独立窗口运行 · 不修改游戏文件", Font = new Font(Font.FontFamily, 10.5f), ForeColor = Muted, BackColor = Color.Transparent, Location = new Point(24, 113), Size = new Size(800, 32) });
        body.Controls.Add(banner, 0, 0); body.SetColumnSpan(banner, 2);
        var main = new GlowPanel { Dock = DockStyle.Fill, Radius = 22, BackColor = Surface, Padding = new Padding(28), Margin = new Padding(0, 0, 18, 16) };
        var startContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.Transparent };
        startContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); startContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        startContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); startContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); startContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var magpieHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        magpieHeading.Controls.Add(new Label { Text = "\uE945", Location = new Point(0, 8), Size = new Size(56, 53),
            Font = new Font("Segoe MDL2 Assets", 26), ForeColor = Acid, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
        magpieHeading.Controls.Add(new Label { Text = "一键开启大力喜鹊", Location = new Point(62, 8), Size = new Size(550, 53),
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
        startContent.Controls.Add(magpieHeading, 0, 0);
        startContent.SetColumnSpan(startContent.Controls[0], 2);
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        magpieStart = Action("开启大力喜鹊", "Start Magpie", async (_, _) => await StartMagpieAsync());
        magpieStart.Size = new Size(620, 66); magpieStart.Radius = 18; magpieStart.BackColor = Acid; magpieStart.ForeColor = OnAccent;
        magpieStart.Font = new Font(Font.FontFamily, 16, FontStyle.Bold); magpieStart.LightningEffect = true;
        left.Controls.Add(magpieStart); left.Controls.Add(magpieStatus); left.Controls.Add(magpieProgress);
        left.ClientSizeChanged += (_, _) =>
        {
            magpieStart.Width = Math.Max(180, Math.Min(620, left.ClientSize.Width - 10));
            magpieProgress.Width = Math.Max(180, Math.Min(620, left.ClientSize.Width - 10));
        };
        startContent.Controls.Add(left, 0, 1); startContent.SetRowSpan(left, 2);
        var features = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        features.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); features.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var featureItems = new[]
        {
            ("\uE896", "自动下载依赖", "包含 Magpie 与补丁文件"),
            ("\uE73E", "校验组件", "下载后自动验证"),
            ("\uE8A7", "独立窗口运行", "无需修改游戏窗口"),
            ("\uE72A", "不改游戏文件", "支持安全恢复")
        };
        for (var i = 0; i < featureItems.Length; i++)
        {
            var feature = new RoundedPanel { Dock = DockStyle.Fill, Radius = 16, BackColor = Sidebar, Margin = new Padding(5) };
            var badge = new RoundedPanel { Location = new Point(11, 16), Size = new Size(46, 46), Radius = 23,
                BackColor = SelectedSurface, BorderColor = Line };
            badge.Controls.Add(new Label { Text = featureItems[i].Item1, Dock = DockStyle.Fill,
                Font = new Font("Segoe MDL2 Assets", 17), ForeColor = Acid, TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent });
            feature.Controls.Add(badge);
            var featureTitle = new Label { Text = featureItems[i].Item2, Location = new Point(62, 13), Size = new Size(230, 26),
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent };
            var featureCaption = new Label { Text = featureItems[i].Item3, Location = new Point(62, 41), Size = new Size(230, 27),
                Font = new Font(Font.FontFamily, 8.5f), ForeColor = Muted, BackColor = Color.Transparent, AutoEllipsis = true };
            feature.Controls.Add(featureTitle); feature.Controls.Add(featureCaption);
            feature.ClientSizeChanged += (_, _) =>
            {
                var textWidth = Math.Max(60, feature.ClientSize.Width - 72);
                featureTitle.Width = textWidth; featureCaption.Width = textWidth;
            };
            void TrackFeature(Control control)
            {
                control.MouseEnter += (_, _) => feature.Hovered = true;
                control.MouseLeave += (_, _) =>
                {
                    if (!feature.ClientRectangle.Contains(feature.PointToClient(Cursor.Position))) feature.Hovered = false;
                };
                foreach (Control child in control.Controls) TrackFeature(child);
            }
            TrackFeature(feature);
            features.Controls.Add(feature, i % 2, i / 2);
        }
        startContent.Controls.Add(features, 1, 1); startContent.SetRowSpan(features, 2);
        main.Controls.Add(startContent); body.Controls.Add(main, 0, 1);
        var instructions = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Sidebar, BorderColor = Line, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 16) };
        var steps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent };
        steps.Paint += (_, e) =>
        {
            var row = (steps.Height - 42) * .28f;
            using var line = new Pen(Color.FromArgb(85, Acid), 2f);
            e.Graphics.DrawLine(line, 23, 42 + row * .5f, 23, 42 + row * 2.5f);
        };
        steps.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (var i = 0; i < 3; i++) steps.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        steps.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        steps.Controls.Add(new Label { Text = "使用说明", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), ForeColor = Ink }, 0, 0);
        var stepText = new[]
        {
            ("点击开启并等待组件准备完成", "自动下载所需组件并校验"),
            ("将游戏设置为窗口模式", "通过独立窗口模式运行"),
            ("切回游戏，按 Alt + Shift + A", "开启缩放，开始游戏")
        };
        for (var i = 0; i < stepText.Length; i++)
        {
            var step = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var number = new RoundedPanel { Location = new Point(5, 12), Size = new Size(36, 36), Radius = 18,
                BackColor = Acid, BorderColor = Acid };
            number.Controls.Add(new Label { Text = (i + 1).ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = OnAccent, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), BackColor = Color.Transparent });
            step.Controls.Add(number);
            var stepTitle = new Label { Text = stepText[i].Item1, Location = new Point(53, 5), Size = new Size(320, 27),
                ForeColor = Ink, Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold), BackColor = Color.Transparent, AutoEllipsis = true };
            var stepCaption = new Label { Text = stepText[i].Item2, Location = new Point(53, 32), Size = new Size(320, 24),
                ForeColor = Muted, Font = new Font(Font.FontFamily, 8.5f), BackColor = Color.Transparent, AutoEllipsis = true };
            step.Controls.Add(stepTitle); step.Controls.Add(stepCaption);
            step.ClientSizeChanged += (_, _) =>
            {
                var textWidth = Math.Max(80, step.ClientSize.Width - 58);
                stepTitle.Width = textWidth; stepCaption.Width = textWidth;
            };
            steps.Controls.Add(step, 0, i + 1);
        }
        steps.Controls.Add(new Label { Text = "默认 Lanczos 缩放；不会自动开启 DLSS 或补帧。", Dock = DockStyle.Fill, ForeColor = Muted,
            Font = new Font(Font.FontFamily, 9f), Padding = new Padding(5, 0, 0, 0) }, 0, 4);
        instructions.Controls.Add(steps); body.Controls.Add(instructions, 1, 1);
        var configured = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Surface, BorderColor = Line, Padding = new Padding(18), Margin = new Padding(0, 0, 18, 16) };
        configured.Controls.Add(magpieConfiguredCards);
        configured.Controls.Add(new Label { Text = "已配置的游戏", Dock = DockStyle.Top, Height = 46, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), ForeColor = Ink });
        body.Controls.Add(configured, 0, 2);
        RoundedPanel InfoPanel(string title, string[] items, bool warning)
        {
            var panel = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Surface,
                BorderColor = Line, Padding = new Padding(22), Margin = new Padding(0, 0, 0, 16) };
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = items.Length + 1,
                BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            for (var j = 0; j < items.Length; j++) content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / items.Length));
            content.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = warning ? Color.FromArgb(255, 124, 91) : Ink,
                Font = new Font(Font.FontFamily, 13, FontStyle.Bold), BackColor = Color.Transparent }, 0, 0);
            for (var j = 0; j < items.Length; j++)
            {
                var row = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
                row.Controls.Add(new Label { Text = warning ? "\uE7BA" : "\uE73E", Location = new Point(0, 2), Size = new Size(29, 28),
                    TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe MDL2 Assets", 12),
                    ForeColor = warning ? Color.FromArgb(255, 161, 101) : Acid, BackColor = Color.Transparent });
                var rowText = new Label { Text = items[j], Location = new Point(32, 1), Size = new Size(340, 34),
                    Font = new Font(Font.FontFamily, 9.5f), ForeColor = warning ? Muted : Ink, BackColor = Color.Transparent,
                    AutoEllipsis = true };
                row.Controls.Add(rowText);
                row.ClientSizeChanged += (_, _) => rowText.Width = Math.Max(75, row.ClientSize.Width - 36);
                content.Controls.Add(row, 0, j + 1);
            }
            panel.Controls.Add(content);
            return panel;
        }
        var featuresPanel = InfoPanel("功能特点", new[] { "支持 AMD 显卡", "独立窗口模式运行", "自动下载并校验文件", "不修改原游戏文件" }, false);
        body.Controls.Add(featuresPanel, 1, 2);
        var recent = new RoundedPanel { Dock = DockStyle.Fill, Radius = 20, BackColor = Surface, BorderColor = Line, Padding = new Padding(18), Margin = new Padding(0, 0, 20, 0) };
        recent.Controls.Add(magpieGameCards);
        recent.Controls.Add(new Label { Text = "最近的游戏", Dock = DockStyle.Top, Height = 46, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), ForeColor = Ink });
        body.Controls.Add(recent, 0, 3);
        var notes = InfoPanel("注意事项", new[] { "不适用于联机或反作弊游戏", "实际效果取决于显卡和环境", "请勿叠加多套补帧方案" }, true);
        notes.Margin = Padding.Empty;
        body.Controls.Add(notes, 1, 3);
        magpiePage.Controls.Add(body); pageHost.Controls.Add(magpiePage);
        magpieConfiguredCards.ClientSizeChanged += (_, _) => ResizeMagpieTiles();
        magpieGameCards.ClientSizeChanged += (_, _) => ResizeMagpieTiles();
        RenderMagpieGames();
    }

    void RenderMagpieGames()
    {
        foreach (var host in new[] { magpieConfiguredCards, magpieGameCards })
            while (host.Controls.Count > 0) { var c = host.Controls[0]; host.Controls.Remove(c); c.Dispose(); }
        var configured = libraryGames.Where(g => IsConfigured(g.ExePath)).Take(3).ToList();
        if (configured.Count == 0)
        {
            var empty = new Panel { Name = "magpieEmpty", Height = 180, BackColor = Color.Transparent, Margin = Padding.Empty };
            var emptyIcon = new Label { Text = "\uE7FC", Height = 57,
                TextAlign = ContentAlignment.BottomCenter, Font = new Font("Segoe MDL2 Assets", 22),
                ForeColor = Acid, BackColor = Color.Transparent };
            var emptyTitle = new Label { Text = "还没有已配置的游戏", Height = 38,
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
                ForeColor = Ink, BackColor = Color.Transparent };
            empty.Controls.Add(emptyIcon); empty.Controls.Add(emptyTitle);
            var browse = Action("前往游戏库", "Open library", async (_, _) => await NavigateAuthorizedAsync(1));
            browse.Radius = 14; browse.BackColor = SelectedSurface; browse.ForeColor = Acid;
            empty.Controls.Add(browse);
            void PlaceBrowse()
            {
                emptyIcon.SetBounds(0, 0, empty.Width, 57);
                emptyTitle.SetBounds(0, 61, empty.Width, 38);
                browse.SetBounds(Math.Max(0, (empty.Width - 142) / 2), 111, 142, 40);
            }
            empty.SizeChanged += (_, _) => PlaceBrowse();
            magpieConfiguredCards.Controls.Add(empty);
            PlaceBrowse();
        }
        foreach (var game in configured) magpieConfiguredCards.Controls.Add(MagpieTile(game, 238, 172));
        foreach (var game in libraryGames.Take(6)) magpieGameCards.Controls.Add(MagpieTile(game, 152, 164));
        if (libraryGames.Count == 0)
            magpieGameCards.Controls.Add(new Label { Text = "尚未扫描到游戏。", ForeColor = Muted, AutoSize = true, Margin = new Padding(12, 55, 0, 0) });
        ResizeMagpieTiles();
    }

    void ResizeMagpieTiles()
    {
        void Fit(FlowLayoutPanel host, int columns)
        {
            if (host.ClientSize.Width < 300) return;
            var width = Math.Max(120, (host.ClientSize.Width - columns * 14) / columns);
            foreach (Control tile in host.Controls)
            {
                if (tile is HoverLiftSlot) tile.Width = width;
                else if (tile.Name == "magpieEmpty") tile.Width = Math.Max(200, host.ClientSize.Width - 4);
            }
        }
        Fit(magpieConfiguredCards, 3);
        Fit(magpieGameCards, 6);
    }

    Control MagpieTile(GameCandidate game, int width, int height)
    {
        Image image;
        if (game.CoverPath is { } path && File.Exists(path)) { using var source = Image.FromFile(path); image = new Bitmap(source); }
        else image = game.Icon?.ToBitmap() ?? SystemIcons.Application.ToBitmap();
        var tile = new RoundedPanel { Size = new Size(width, height), Radius = 16, BackColor = Surface, Margin = Padding.Empty, Cursor = Cursors.Hand, AccessibleName = game.Title + "，查看游戏" };
        var cover = new CoverPictureBox { Dock = DockStyle.Fill, Image = image, Tag = game.CoverPath != null, BackColor = Surface, Cursor = Cursors.Hand, AccessibleName = game.Title };
        cover.Disposed += (_, _) => cover.Image?.Dispose();
        var name = new Label { Text = game.Title, Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(10, 8, 6, 0), BackColor = Color.Transparent, ForeColor = Ink, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), AutoEllipsis = true, Cursor = Cursors.Hand };
        async void Open(object? _, EventArgs __) { if (!await RequireFeatureAsync("library.manage")) return; SwitchPage(1); RevealGame(game); var target = games.Controls.Cast<Control>().FirstOrDefault(c => ReferenceEquals(c.Tag, game)); if (target != null) games.ScrollControlIntoView(target); }
        tile.Click += Open; cover.Click += Open; name.Click += Open;
        tile.Controls.Add(cover); tile.Controls.Add(name);
        return new HoverLiftSlot { Size = new Size(width, height + 9), Content = tile,
            Margin = new Padding(0, 0, 14, 0) };
    }

    async Task StartMagpieAsync()
    {
        if (busy) { MessageBox.Show(this, "请等待当前操作完成后再开启大力喜鹊。", Text); return; }
        if (!await RequireFeatureAsync("magpie.launch") || busy) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
        { MessageBox.Show(this, "此集成需要 Windows 10 2004 或更新版本的 x64 系统。", Text); return; }
        // Upstream is single-instance: do not silently switch another installation's settings.
        var processes = Process.GetProcessesByName("Magpie");
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        if (running) { magpieStatus.Text = "Magpie 已在运行。请切回游戏按其快捷键启用效果；如需使用 MU 独立配置，请先从托盘退出已有 Magpie。"; return; }
        busy = true; magpieStart!.Enabled = false; UpdateButtons();
        magpieProgress.Visible = true; magpieProgress.Indeterminate = true;
        magpieStatus.Text = "正在检查大力喜鹊组件…";
        using var session = BeginDownloadSession("大力喜鹊 · Magpie", StartMagpieAsync, "magpie.launch");
        try
        {
            var phase = new Progress<string>(text => { magpieStatus.Text = text; magpieProgress.Indeterminate = true; });
            var progress = new Progress<int>(value =>
            {
                magpieProgress.Indeterminate = value >= 100;
                magpieProgress.Value = Math.Clamp(value, 0, 100);
                magpieStatus.Text = value < 100 ? $"正在下载大力喜鹊 · {value}%" : "下载完成，正在校验与准备组件…";
            });
            var exe = await MagpieIntegration.PrepareAsync(progress, phase, lifetime.Token);
            var portableConfig = Path.Combine(Path.GetDirectoryName(exe)!, "config", "v4e", "config.json");
            Core.RejectLinks(portableConfig);
            if (!File.Exists(portableConfig)) throw new IOException("独立配置缺失，已停止启动以免使用其他 Magpie 的全局设置。");
            if (!await RequireFeatureAsync("magpie.launch")) return;
            using var child = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            if (child == null) throw new IOException("未能启动 Magpie。");
            magpieProgress.Indeterminate = false; magpieProgress.Value = 100;
            magpieStatus.Text = "已发起启动。切回窗口模式的游戏，按 Alt + Shift + A 启用效果（不代表效果已生效）。";
        }
        catch (OperationCanceledException) { magpieProgress.Indeterminate = false; magpieProgress.Visible = false; magpieStatus.Text = "下载已取消，可重新开启。"; }
        catch (Exception e) { magpieProgress.Indeterminate = false; magpieProgress.Visible = false; magpieStatus.Text = "开启失败：" + e.Message; }
        finally { busy = false; magpieStart.Enabled = true; UpdateButtons(); }
    }
}
