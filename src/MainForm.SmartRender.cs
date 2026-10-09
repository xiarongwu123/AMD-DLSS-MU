using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    string optiFgInput = "nofg";

    Form SmartWindow(string title, int width, int height)
    {
        var form = new Form
        {
            Text = title, ClientSize = new Size(width, height), Font = Font,
            BackColor = Line, ForeColor = Ink, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, MinimizeBox = false,
            MaximizeBox = false, KeyPreview = true,
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi
        };
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.DialogResult = DialogResult.Cancel; };
        form.SizeChanged += (_, _) =>
        {
            if (form.Width < 4 || form.Height < 4) return;
            using var path = UiPaint.RoundPath(new RectangleF(0, 0, form.Width, form.Height), 18);
            var old = form.Region;
            form.Region = new Region(path);
            old?.Dispose();
        };
        ThemeWindow(form);
        form.BackColor = Line;
        return form;
    }

    DialogResult ShowSmartModal(Form dialog)
    {
        using var scrim = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = Bounds, BackColor = Color.Black, Opacity = .66,
            ShowInTaskbar = false, TopMost = false
        };
        scrim.Show(this);
        try
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(scrim);
        }
        finally { scrim.Close(); }
    }

    Label SmartText(string text, float size, Color color, bool bold = false)
        => new()
        {
            Text = text, ForeColor = color, BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };

    RoundedPanel SmartCard(Color? fill = null, Color? border = null, int radius = 17)
        => new()
        {
            Radius = radius, BackColor = fill ?? Sidebar, BorderColor = border ?? Line,
            Margin = Padding.Empty
        };

    Panel SmartBanner(Form dialog, string title, string subtitle, string symbol)
    {
        var banner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = darkMode ? Color.FromArgb(16, 25, 31) : Sidebar,
            Margin = Padding.Empty
        };
        banner.Paint += (_, e) =>
        {
            if (banner.Width < 2 || banner.Height < 2) return;
            using var gradient = new LinearGradientBrush(banner.ClientRectangle,
                darkMode ? Color.FromArgb(16, 25, 31) : Sidebar,
                darkMode ? Color.FromArgb(19, 31, 34) : Surface, 0f);
            e.Graphics.FillRectangle(gradient, banner.ClientRectangle);
            using var rule = new Pen(Line, 1);
            e.Graphics.DrawLine(rule, 0, banner.Height - 1, banner.Width, banner.Height - 1);
        };
        var icon = SmartText(symbol, 23, Acid, true);
        icon.Name = "smartBannerIcon";
        icon.TextAlign = ContentAlignment.MiddleCenter;
        icon.BackColor = SelectedSurface;
        icon.SetBounds(35, 33, 50, 50);
        var heading = SmartText(title, 23, Ink, true);
        heading.Name = "smartBannerTitle";
        heading.SetBounds(103, 22, 650, 52);
        var caption = SmartText(subtitle, 10, Muted);
        caption.Name = "smartBannerCaption";
        caption.SetBounds(105, 70, 700, 30);
        var close = new SmartCloseButton { AccessibleName = english ? "Close" : "关闭" };
        close.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        close.SetBounds(dialog.ClientSize.Width - 59, 21, 38, 38);
        close.BackColor = Sidebar; close.ForeColor = Ink;
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        banner.Controls.Add(icon); banner.Controls.Add(heading); banner.Controls.Add(caption); banner.Controls.Add(close);
        banner.Resize += (_, _) =>
        {
            var scale = banner.DeviceDpi / 96f;
            close.SetBounds(banner.ClientSize.Width - (int)(58 * scale), (int)(21 * scale),
                (int)(38 * scale), (int)(38 * scale));
        };
        void Drag(object? _, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !OperatingSystem.IsWindows()) return;
            ReleaseCapture();
            SendMessage(dialog.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        banner.MouseDown += Drag; heading.MouseDown += Drag; caption.MouseDown += Drag;
        return banner;
    }

    bool ChooseSmartRender(string exe, SmartRenderGoal initialGoal = SmartRenderGoal.Quality, bool previewOnly = false)
    {
        using var dialog = new SmartRecommendationDialog(english, initialGoal, previewOnly);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var detecting = false;
        dialog.FormClosed += (_, _) => cancellation.Cancel();
        dialog.DetectionRequested += async (_, _) =>
        {
            if (detecting || cancellation.IsCancellationRequested) return;
            detecting = true;
            dialog.ShowChecking();
            try
            {
                await Task.Yield();
                var gpus = await new CompatibilityEnvironmentReader().ReadGpusAsync(cancellation.Token);
                if (dialog.IsDisposed || cancellation.IsCancellationRequested) return;
                var gpu = SmartRenderAdvisor.SelectGpu(gpus);
                if (gpu == null || string.IsNullOrWhiteSpace(gpu.Name) || gpu.Name == CompatibilityEnvironmentReader.Unknown)
                {
                    dialog.SetFailure(english ? "GPU information could not be read. Retry or choose manually." :
                        "没有读到完整的显卡信息。可以重新检测，或手动选择方案。", true);
                    return;
                }
                dialog.ReportComponents();
                var checks = await Task.Run(() =>
                {
                    var standard = GameManagement.Check(exe, true, false, 2);
                    var rx6000 = GameManagement.HasRx6000([gpu.Name]);
                    var hip = rx6000 ? HipRuntime.HasVersion72() : SmartRenderDiagnostics.HasHipRuntime();
                    // RX 6000 may proceed to the official HIP 7.2 setup before game configuration.
                    var hipCanBePrepared = rx6000 && !hip;
                    return (Standard: standard,
                        Quality: (hip || hipCanBePrepared) && !GameManagement.Check(exe, true, true, 0).Blocked,
                        Combined: (hip || hipCanBePrepared) && !GameManagement.Check(exe, true, true, 4).Blocked,
                        HipSetupPending: hipCanBePrepared);
                }, cancellation.Token);
                if (dialog.IsDisposed || cancellation.IsCancellationRequested) return;
                if (checks.Standard.Blocked)
                {
                    dialog.SetFailure(string.Join(Environment.NewLine, checks.Standard.BlockingReasons), false);
                    return;
                }
                dialog.SetResult(gpu, SmartRenderAdvisor.Recommend(gpu, checks.Quality, checks.Combined),
                    Screen.FromControl(this).Bounds.Size, checks.HipSetupPending);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!dialog.IsDisposed && !cancellation.IsCancellationRequested)
                {
                    Diagnostics.Record(exe, "smart-render", "detection-failed", error.Message);
                    dialog.SetFailure(english ? "The environment check did not finish. Retry or choose manually." :
                        "本机环境检查未完成。可以重新检测，或手动选择方案。", true);
                }
            }
            finally { detecting = false; }
        };
        if (ShowSmartModal(dialog) != DialogResult.OK || dialog.SelectedMode == null) return false;
        installMode.SelectedIndex = dialog.SelectedMode switch { 0 => 0, 4 => 2, _ => 1 };
        // Custom OptiScaler preserves settings chosen through the existing advanced controls.
        if (dialog.SelectedGoal != SmartRenderGoal.Custom || dialog.SelectedMode != 2)
        {
            optiVulkan = false;
            optiFgInput = "nofg";
        }
        Diagnostics.Record(exe, "smart-render", "selected", dialog.SelectedGoal + " / " + SelectedInstallMode + " / FG " + optiFgInput);
        return true;
    }

    async Task ShowQuickDiagnosticsAsync(string exe)
    {
        if (busy || !await RequireFeatureAsync("diagnostics.use")) return;
        using var dialog = SmartWindow(english ? "One-click check" : "一键排查", 930, 775);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Line, Margin = Padding.Empty, Padding = new Padding(2) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        var banner = SmartBanner(dialog, english ? "Checking…" : "正在检查…",
            english ? "Reading this game's configuration and local components." : "正在读取游戏配置与本机组件",
            "·");
        var bannerTitle = (Label)banner.Controls["smartBannerTitle"]!;
        var bannerCaption = (Label)banner.Controls["smartBannerCaption"]!;
        var bannerIcon = (Label)banner.Controls["smartBannerIcon"]!;
        banner.Controls.Remove(bannerIcon);
        var statusRing = SmartCard(SelectedSurface, Acid, 33);
        statusRing.SetBounds(35, 34, 55, 55);
        bannerIcon.Dock = DockStyle.None;
        bannerIcon.BackColor = Color.Transparent;
        bannerIcon.SetBounds(5, 5, 45, 45);
        bannerIcon.Text = "✓";
        bannerIcon.TextAlign = ContentAlignment.MiddleCenter;
        statusRing.Controls.Add(bannerIcon);
        banner.Controls.Add(statusRing);
        bannerTitle.Left = 108;
        bannerCaption.Left = 110;
        root.Controls.Add(banner, 0, 0);

        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
            FlowDirection = FlowDirection.TopDown, BackColor = Base, Padding = new Padding(32, 6, 18, 8),
            Margin = Padding.Empty };
        list.HandleCreated += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) SetWindowTheme(list.Handle, darkMode ? "DarkMode_Explorer" : "Explorer", null);
        };
        root.Controls.Add(list, 0, 1);
        var rows = new List<RoundedPanel>();
        void ResizeRows()
        {
            var width = Math.Max(300, list.ClientSize.Width - list.Padding.Horizontal - 18);
            foreach (var row in rows) row.Width = width;
        }
        list.Resize += (_, _) => ResizeRows();

        void AddCheck(SmartCheck check)
        {
            var failed = check.Problem && !check.Unknown;
            var color = failed ? Color.FromArgb(255, 138, 105) : check.Unknown ? Muted : Acid;
            var state = failed ? (english ? "NEEDS FIX" : "需处理") :
                check.Unknown ? (english ? "VERIFY" : "待验证") : (english ? "PASSED" : "已通过");
            var card = SmartCard(Sidebar, check.Problem ? color : Line, 15);
            card.Height = 78;
            card.Margin = new Padding(0, 0, 0, 8);
            card.Cursor = Cursors.Hand; card.TabStop = true;
            var icon = new SmartLineIcon(check.Title switch
            {
                "显卡" => "chip", "驱动" => "gear", "安装记录" => "file", "方案设置" => "sliders",
                "XeFG 组件" => "puzzle", "FSR 帧生成组件" => "puzzle", "游戏加载组件" => "folder",
                "游戏图形接口" => "display", _ => "file"
            }, color);
            icon.SetBounds(19, 16, 50, 46);
            var title = SmartText(check.Title, 12, Ink, true);
            title.SetBounds(83, 9, 610, 30);
            var brief = SmartText(check.Detail, 9, Muted);
            brief.SetBounds(83, 39, 610, 27);
            var statusLabel = SmartText(state, 10, color, true);
            statusLabel.TextAlign = ContentAlignment.MiddleRight;
            statusLabel.AutoEllipsis = false;
            var arrow = SmartText("⌄", 16, Muted);
            arrow.TextAlign = ContentAlignment.MiddleCenter;
            arrow.AutoEllipsis = false;
            var expanded = SmartText(check.Detail + (check.Unknown
                ? (english ? "  Confirm the actual result in game." : "  请进游戏确认实际效果。")
                : ""), 10, Muted);
            expanded.Visible = false;
            card.Controls.Add(icon); card.Controls.Add(title); card.Controls.Add(brief);
            card.Controls.Add(statusLabel); card.Controls.Add(arrow); card.Controls.Add(expanded);
            void Layout()
            {
                var width = card.Width;
                int D(int value) => (int)Math.Round(value * card.DeviceDpi / 96f);
                title.Width = Math.Max(D(100), width - D(315));
                brief.Width = title.Width;
                statusLabel.SetBounds(width - D(190), D(16), D(135), D(44));
                arrow.SetBounds(width - D(45), D(16), D(25), D(44));
                expanded.SetBounds(D(83), D(82), Math.Max(D(100), width - D(115)), D(52));
            }
            card.Resize += (_, _) => Layout();
            void Toggle(object? _, EventArgs __)
            {
                expanded.Visible = !expanded.Visible;
                card.Height = (int)Math.Round((expanded.Visible ? 142 : 78) * card.DeviceDpi / 96f);
                arrow.Text = expanded.Visible ? "⌃" : "⌄";
            }
            card.Click += Toggle;
            foreach (Control child in card.Controls) child.Click += Toggle;
            card.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Space) { Toggle(card, EventArgs.Empty); e.Handled = true; }
            };
            rows.Add(card); list.Controls.Add(card); ResizeRows(); Layout();
        }

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, ColumnCount = 3,
            RowCount = 2, Margin = Padding.Empty, Padding = new Padding(32, 6, 32, 12) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var note = SmartText(english ? "Files found does not prove the effect is active. Verify in game." :
            "文件齐全不代表效果已生效；启动游戏后可在“查看诊断”刷新运行状态。", 9, Muted);
        note.Dock = DockStyle.Fill; note.Margin = Padding.Empty;
        var close = Action("关闭", "Close", (_, _) => dialog.DialogResult = DialogResult.Cancel);
        close.BackColor = Sidebar; close.ForeColor = Ink; close.Radius = 13;
        close.Dock = DockStyle.Fill; close.Margin = new Padding(0, 0, 12, 0);
        var next = Action("继续配置", "Continue", (_, _) => dialog.DialogResult = DialogResult.OK);
        next.BackColor = Acid; next.ForeColor = OnAccent; next.Radius = 13;
        next.Dock = DockStyle.Fill; next.Margin = Padding.Empty;
        footer.Controls.Add(note, 0, 0); footer.SetColumnSpan(note, 3);
        footer.Controls.Add(close, 1, 1); footer.Controls.Add(next, 2, 1);
        root.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(root);
        dialog.Shown += async (_, _) =>
        {
            try
            {
                var checks = await Task.Run(() => SmartRenderDiagnostics.Check(exe));
                if (dialog.IsDisposed) return;
                var problems = checks.Count(x => x.Problem && !x.Unknown);
                var unknown = checks.Count(x => x.Unknown);
                statusRing.BorderColor = problems > 0 ? Color.FromArgb(255, 138, 105) : Acid;
                bannerIcon.ForeColor = statusRing.BorderColor;
                bannerIcon.Text = problems > 0 ? "!" : unknown > 0 ? "·" : "✓";
                bannerTitle.Text = problems > 0
                    ? (english ? $"{problems} item(s) need attention" : $"发现 {problems} 项需要处理")
                    : unknown > 0 ? (english ? "Basic check complete" : "基础检查已完成")
                    : (english ? "Basic checks passed" : "基础检查通过");
                bannerCaption.Text = problems > 0
                    ? (english ? "Open each item for details before configuration." : "请查看未通过的项目，再继续配置。")
                    : unknown > 0 ? (english ? $"{unknown} item(s) still need in-game verification." : $"{unknown} 项仍需在游戏中验证。")
                    : (english ? "The local environment meets the basic requirements." : "当前环境已满足基础运行条件，可继续配置与验证。");
                list.SuspendLayout();
                try { foreach (var check in checks) AddCheck(check); }
                finally { list.ResumeLayout(true); }
            }
            catch (Exception e)
            {
                if (dialog.IsDisposed) return;
                bannerTitle.Text = english ? "Check incomplete" : "排查未完成";
                bannerCaption.Text = english ? "The check could not be completed." : "本次检查未完成，请查看下方说明。";
                bannerIcon.Text = "!";
                bannerIcon.ForeColor = Color.FromArgb(255, 138, 105);
                statusRing.BorderColor = bannerIcon.ForeColor;
                // The fallback must not construct the same custom controls that may have failed.
                while (list.Controls.Count > 0)
                {
                    var child = list.Controls[0];
                    list.Controls.Remove(child);
                    child.Dispose();
                }
                rows.Clear();
                var error = new Label
                {
                    AutoSize = true, MaximumSize = new Size(Math.Max(280, list.ClientSize.Width - 90), 0),
                    BackColor = Surface, ForeColor = Ink, Padding = new Padding(18),
                    Text = english ? "Check incomplete: " + e.Message : "检查未完成：" + e.Message
                };
                list.Controls.Add(error);
                next.Enabled = false;
                Diagnostics.Record(exe, "smart-render-check", "error", e.ToString());
            }
        };
        if (ShowSmartModal(dialog) != DialogResult.OK) return;
        selectedGame = exe;
        await OpenSmartRenderForSelectedAsync();
    }
}

internal sealed class SmartCloseButton : Button
{
    bool hovered;
    public SmartCloseButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Text = "";
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = UiPaint.RoundPath(new RectangleF(1, 1, Width - 2, Height - 2), 9 * DeviceDpi / 96f);
        using var brush = new SolidBrush(hovered ? MainForm.Line : BackColor);
        e.Graphics.FillPath(brush, shape);
        var extent = Math.Min(Width, Height) * .16f;
        var cx = Width / 2f;
        var cy = Height / 2f;
        using var pen = new Pen(ForeColor, Math.Max(1.7f, 1.7f * DeviceDpi / 96f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(pen, cx - extent, cy - extent, cx + extent, cy + extent);
        e.Graphics.DrawLine(pen, cx + extent, cy - extent, cx - extent, cy + extent);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}

internal sealed class SmartLineIcon : Control
{
    readonly string kind;
    readonly Color stroke;

    public SmartLineIcon(string kind, Color stroke)
    {
        this.kind = kind;
        this.stroke = stroke;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 8 || Height < 8) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height);
        g.TranslateTransform((Width - size) / 2f, (Height - size) / 2f);
        g.ScaleTransform(size / 40f, size / 40f);
        using var pen = new Pen(stroke, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(stroke);
        void Rounded(float x, float y, float w, float h, float r = 3)
        {
            using var path = UiPaint.RoundPath(new RectangleF(x, y, w, h), r);
            g.DrawPath(pen, path);
        }
        switch (kind)
        {
            case "chip":
                Rounded(9, 9, 22, 22);
                Rounded(15, 15, 10, 10, 2);
                foreach (var n in new[] { 14, 20, 26 })
                {
                    g.DrawLine(pen, n, 5, n, 9); g.DrawLine(pen, n, 31, n, 35);
                    g.DrawLine(pen, 5, n, 9, n); g.DrawLine(pen, 31, n, 35, n);
                }
                break;
            case "display":
                Rounded(5, 7, 30, 23);
                g.DrawLine(pen, 20, 30, 20, 35); g.DrawLine(pen, 13, 35, 27, 35);
                break;
            case "target":
                g.DrawEllipse(pen, 6, 6, 28, 28); g.DrawEllipse(pen, 13, 13, 14, 14);
                g.FillEllipse(fill, 18, 18, 4, 4);
                break;
            case "image":
                Rounded(6, 7, 28, 26);
                g.DrawLines(pen, new Point[] { new(9, 28), new(17, 20), new(21, 24), new(27, 17), new(32, 23) });
                g.FillEllipse(fill, 12, 12, 4, 4);
                break;
            case "bars":
                g.FillRectangle(fill, 8, 24, 5, 10); g.FillRectangle(fill, 18, 17, 5, 17);
                g.FillRectangle(fill, 28, 9, 5, 25);
                break;
            case "sliders":
                foreach (var y in new[] { 10, 20, 30 }) g.DrawLine(pen, 6, y, 34, y);
                g.FillEllipse(fill, 11, 7, 6, 6); g.FillEllipse(fill, 24, 17, 6, 6);
                g.FillEllipse(fill, 16, 27, 6, 6);
                break;
            case "gear":
                g.DrawEllipse(pen, 10, 10, 20, 20); g.DrawEllipse(pen, 16, 16, 8, 8);
                foreach (var angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                {
                    var a = angle * Math.PI / 180d;
                    g.DrawLine(pen, 20 + (int)(11 * Math.Cos(a)), 20 + (int)(11 * Math.Sin(a)),
                        20 + (int)(16 * Math.Cos(a)), 20 + (int)(16 * Math.Sin(a)));
                }
                break;
            case "file":
                g.DrawLines(pen, new Point[] { new(10, 5), new(24, 5), new(31, 12),
                    new(31, 35), new(10, 35), new(10, 5) });
                g.DrawLine(pen, 24, 5, 24, 12); g.DrawLine(pen, 24, 12, 31, 12);
                g.DrawLine(pen, 15, 20, 26, 20); g.DrawLine(pen, 15, 26, 26, 26);
                break;
            case "folder":
                g.DrawLines(pen, new Point[] { new(5, 13), new(16, 13), new(20, 17),
                    new(35, 17), new(35, 33), new(5, 33), new(5, 13) });
                break;
            case "shield":
                g.DrawLines(pen, new Point[] { new(20, 4), new(33, 9), new(31, 25),
                    new(20, 36), new(9, 25), new(7, 9), new(20, 4) });
                g.DrawLines(pen, new Point[] { new(14, 20), new(18, 24), new(26, 15) });
                break;
            default:
                Rounded(7, 7, 26, 26);
                g.DrawLine(pen, 13, 20, 27, 20);
                break;
        }
    }
}
