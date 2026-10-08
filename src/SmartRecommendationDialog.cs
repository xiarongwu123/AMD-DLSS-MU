using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mu.Compatibility;

namespace AmdNrAssistant;

// Logical pixels and font sizes follow mu-smart-route-prototype.html.
// This dialog deliberately owns its paint styles so global button gradients do not alter it.
internal sealed class SmartRecommendationDialog : Form
{
    internal static readonly Color Canvas = ColorTranslator.FromHtml("#070b10");
    internal static readonly Color Header = ColorTranslator.FromHtml("#111b24");
    internal static readonly Color Surface = ColorTranslator.FromHtml("#121d28");
    internal static readonly Color Rule = ColorTranslator.FromHtml("#253440");
    internal static readonly Color Ink = ColorTranslator.FromHtml("#f1f5f6");
    internal static readonly Color Muted = ColorTranslator.FromHtml("#a4b3bf");
    internal static readonly Color Dim = ColorTranslator.FromHtml("#71818d");
    internal static readonly Color Accent = ColorTranslator.FromHtml("#adff1f");
    readonly bool english, previewOnly;
    readonly SmartRenderGoal initialGoal;
    readonly SmartRenderSelection selection = new();
    readonly Panel header = new(), footer = new(), viewport = new(), content = new();
    readonly Panel hardware = new(), waiting = new(), failed = new();
    readonly List<(Label Label, float Pixels, bool Bold)> typography = new();
    readonly Label title, logo, steps, gpu, gpuDetail, gpuStatus, alert, goalHeading;
    readonly Label summaryPrefix, summaryValue, listHeading, listHint, listNote;
    readonly Label waitTitle, waitStage, waitElapsed, failTitle, failDetail;
    readonly SmartPrototypeButton close, back, configure, retry, manual;
    readonly SmartPrototypeButton[] goals = new SmartPrototypeButton[3];
    readonly SmartPrototypeButton[] routes = new SmartPrototypeButton[3];
    readonly AccentProgressBar progress = new();
    readonly System.Windows.Forms.Timer clock = new() { Interval = 250 };
    readonly ToolTip details = new();
    DateTime started;
    string stage = "";
    bool checking = true, hasFailure, layingOut, hipSetupPending;
    public event EventHandler? DetectionRequested;
    public SmartRenderGoal? SelectedGoal => selection.Goal;
    public int? SelectedMode => selection.Mode;

    string T(string zh, string en) => english ? en : zh;
    float ScaleFactor => DeviceDpi / 96f;
    int P(float value) => (int)Math.Round(value * ScaleFactor);
    void Place(Control control, float x, float y, float width, float height) =>
        control.SetBounds(P(x), P(y), P(width), P(height));

    Label TextLabel(Control parent, string text, float pixels, Color color, bool bold = false)
    {
        var label = new SmartPrototypeLabel { Text = text, ForeColor = color, BackColor = Color.Transparent,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei", pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel) };
        typography.Add((label, pixels, bold));
        parent.Controls.Add(label);
        return label;
    }

    SmartPrototypeButton Button(Control parent, string text, SmartPrototypeButton.Style style = SmartPrototypeButton.Style.Action)
    {
        var button = new SmartPrototypeButton { Text = text, PaintStyle = style };
        parent.Controls.Add(button);
        return button;
    }

    public SmartRecommendationDialog(bool english, SmartRenderGoal initialGoal, bool previewOnly)
    {
        this.english = english; this.initialGoal = initialGoal; this.previewOnly = previewOnly;
        Text = T("智能推荐", "Smart render");
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
        BackColor = Rule; KeyPreview = true;
        header.BackColor = Header;
        footer.BackColor = ColorTranslator.FromHtml("#0d151e");
        viewport.BackColor = content.BackColor = waiting.BackColor = failed.BackColor = Canvas;
        viewport.AutoScroll = true;
        Controls.AddRange([header, viewport, footer]);
        viewport.Controls.Add(content);
        viewport.Controls.Add(waiting);
        viewport.Controls.Add(failed);
        logo = TextLabel(header, "MU", 16, ColorTranslator.FromHtml("#091004"), true);
        logo.BackColor = Accent; logo.TextAlign = ContentAlignment.MiddleCenter;
        title = TextLabel(header, Text, 24, Ink, true);
        close = Button(header, "×", SmartPrototypeButton.Style.Close);
        close.AccessibleName = T("关闭", "Close");
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        header.Paint += (_, e) => { using var pen = new Pen(Rule); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
        footer.Paint += (_, e) => { using var pen = new Pen(Rule); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        void Drag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        header.MouseDown += Drag; title.MouseDown += Drag;

        steps = TextLabel(content, T("1 检测　—　2 选择目标与方案　—　3 一键配置", "1 Check    —    2 Choose goal    —    3 Configure"), 12, Dim);
        hardware.BackColor = Surface;
        content.Controls.Add(hardware);
        gpu = TextLabel(hardware, "", 16, Ink, true);
        gpuDetail = TextLabel(hardware, "", 12, Muted);
        gpuStatus = TextLabel(hardware, "", 12, Accent, true);
        gpuStatus.TextAlign = ContentAlignment.MiddleRight;
        alert = TextLabel(content, "", 14, ColorTranslator.FromHtml("#f1ac8b"), true);
        goalHeading = TextLabel(content, T("你更在意什么？", "What matters most?"), 13, Muted, true);
        string[] goalNames = [T("画质优先", "Quality"), T("帧率优先", "Performance"), T("自定义", "Custom")];
        for (var i = 0; i < goals.Length; i++)
        {
            var goal = (SmartRenderGoal)i;
            goals[i] = Button(content, goalNames[i], SmartPrototypeButton.Style.Goal);
            goals[i].Click += (_, _) => { if (selection.SelectGoal(goal)) Render(); };
        }
        summaryPrefix = TextLabel(content, "", 13, Muted);
        summaryValue = TextLabel(content, "", 13, Accent, true);
        listHeading = TextLabel(content, T("自定义方案", "Custom route"), 15, Ink, true);
        listHint = TextLabel(content, T("选择一个方案", "Choose one route"), 11, Dim);
        listHint.TextAlign = ContentAlignment.MiddleRight;
        string[] routeNames = [T("DLSS 5 神经渲染 · 模式一", "DLSS 5 neural · Mode 1"), "DLSS 5 + XeFG 2X", T("标准 OptiScaler", "Standard OptiScaler")];
        string[] routeNotes = [T("优先画质 · 神经渲染", "Image quality · Neural rendering"), T("优先帧率 · 多帧生成", "Frame rate · Frame generation"), T("兼容方案 · 无需 DLSS 5", "Compatibility route · No DLSS 5")];
        int[] modes = [0, 4, 2];
        for (var i = 0; i < routes.Length; i++)
        {
            var mode = modes[i];
            routes[i] = Button(content, routeNames[i], SmartPrototypeButton.Style.Route);
            routes[i].TopRule = i == 0;
            routes[i].Detail = routeNotes[i];
            routes[i].Click += (_, _) => { if (selection.SelectMode(mode)) Render(); };
        }
        listNote = TextLabel(content, "", 11, Muted);

        waitTitle = TextLabel(waiting, T("正在读取本机信息", "Reading this PC"), 18, Ink, true);
        waitStage = TextLabel(waiting, "", 13, Muted);
        waitElapsed = TextLabel(waiting, "", 12, Muted);
        waiting.Controls.Add(progress);
        failTitle = TextLabel(failed, T("未能完成检测", "Check incomplete"), 18, Ink, true);
        failDetail = TextLabel(failed, "", 13, Muted);
        failDetail.AutoEllipsis = false;
        retry = Button(failed, T("重新检测", "Retry")); retry.Primary = true;
        retry.Click += (_, _) => { if (!checking) DetectionRequested?.Invoke(this, EventArgs.Empty); };
        manual = Button(failed, T("手动选择方案", "Choose manually"));
        manual.Click += (_, _) => { selection.UseManual(); hasFailure = false; gpu.Text = T("显卡未识别", "GPU not confirmed"); gpuDetail.Text = T("手动选择前请确认兼容性", "Confirm compatibility before continuing"); Render(); };
        back = Button(footer, T("返回游戏库", "Game library"));
        back.DialogResult = DialogResult.Cancel;
        configure = Button(footer, T("一键配置", "Configure")); configure.Primary = true;
        configure.Click += (_, _) =>
        {
            if (previewOnly) DialogResult = DialogResult.Cancel;
            else if (selection.CanConfigure) DialogResult = DialogResult.OK;
        };
        CancelButton = back; AcceptButton = configure;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
        clock.Tick += (_, _) =>
        {
            var seconds = (int)(DateTime.UtcNow - started).TotalSeconds;
            waitStage.Text = stage + new string('·', seconds % 3 + 1);
            waitElapsed.Text = T($"已检测 {seconds} 秒", $"Checking for {seconds}s");
        };
        Shown += (_, _) => { Render(); DetectionRequested?.Invoke(this, EventArgs.Empty); };
        DpiChanged += (_, _) => Render();
        Render();
    }

    public void ShowChecking()
    {
        selection.Reset(); checking = true; hasFailure = false;
        started = DateTime.UtcNow;
        stage = T("正在读取显卡", "Reading GPU");
        waitStage.Text = stage; waitElapsed.Text = T("已检测 0 秒", "Checking for 0s");
        progress.Indeterminate = true; clock.Start(); Render();
    }
    public void ReportComponents()
    {
        stage = T("显卡已识别，正在检查 HIP 与游戏文件", "GPU found. Checking HIP and game files");
        waitStage.Text = stage;
    }
    public void SetResult(CompatibilityGpu detected, SmartRenderRecommendation recommendation, Size display, bool requiresHipSetup = false)
    {
        StopChecking(); hasFailure = false;
        hipSetupPending = requiresHipSetup;
        selection.SetRecommendation(recommendation, initialGoal);
        gpu.Text = detected.Name;
        gpuDetail.Text = $"{display.Width} × {display.Height} · " + (detected.VramMb is > 0
            ? $"{detected.VramMb / 1024d:0.#} GB " + T("显存", "VRAM") : T("显存未确认", "VRAM unknown"));
        details.SetToolTip(gpu, detected.Name);
        Render();
    }
    public void SetFailure(string message, bool allowManual)
    {
        StopChecking(); selection.Reset(); hasFailure = true;
        failDetail.Text = message; manual.Visible = allowManual;
        Render();
    }
    void StopChecking() { checking = false; clock.Stop(); progress.Indeterminate = false; }

    string RouteName(int? mode) => mode switch
    {
        0 => T("DLSS 5 模式一", "DLSS 5 Mode 1"), 4 => "DLSS 5 + XeFG 2X",
        2 => T("标准 OptiScaler", "Standard OptiScaler"), _ => ""
    };

    void Render()
    {
        if (layingOut || IsDisposed) return;
        layingOut = true;
        SuspendLayout();
        try
        {
            var rec = selection.Recommendation;
            var unsupported = rec?.Availability == SmartRenderAvailability.HardwareUnsupported;
            var unavailable = rec != null && rec.QualityMode == 2 && rec.PerformanceMode == 2;
            var hasAutomaticRoute = rec?.QualityMode == 0 || rec?.PerformanceMode == 4;
            var custom = selection.ShowSchemeList;
            title.Text = checking ? T("正在检测", "Checking") : T("智能推荐", "Smart render");
            content.Visible = !checking && !hasFailure;
            waiting.Visible = checking; failed.Visible = hasFailure;
            alert.Visible = unavailable || selection.ManualWithoutDetection;
            alert.Text = unsupported ? T("当前显卡不支持开启 DLSS 5", "This GPU cannot enable DLSS 5") :
                T("暂时无法开启 DLSS 5，请先确认显卡、HIP 与游戏条件。", "DLSS 5 unavailable. Check GPU, HIP and game prerequisites.");
            gpuStatus.Text = unsupported ? T("不支持开启 DLSS 5", "DLSS 5 unsupported") :
                selection.ManualWithoutDetection ? T("未完成检测", "Not checked") :
                unavailable ? T("DLSS 5 暂不可用", "DLSS 5 unavailable") :
                hipSetupPending ? T("需安装 HIP 7.2", "HIP 7.2 required") : T("基础检查已完成", "Basic checks complete");
            for (var i = 0; i < goals.Length; i++)
            {
                var candidate = i == 0 ? rec?.QualityMode : rec?.PerformanceMode;
                goals[i].Enabled = i == 2 ? rec != null || selection.ManualWithoutDetection : candidate is 0 or 4;
                goals[i].IsSelected = selection.Goal == (SmartRenderGoal)i;
                goals[i].Detail = i == 2 ? T("从下方列表自行选择", "Choose from the list") :
                    goals[i].Enabled ? T("推荐 · ", "Recommended · ") + RouteName(candidate) :
                    unsupported ? T("当前显卡不可用", "GPU unsupported") : T("当前不可用", "Unavailable");
                goals[i].Invalidate();
            }
            listHeading.Visible = listHint.Visible = listNote.Visible = custom;
            summaryPrefix.Visible = summaryValue.Visible = !custom;
            summaryPrefix.Text = selection.Goal switch
            {
                SmartRenderGoal.Quality => T("画质优先已自动选用：", "Quality route: "),
                SmartRenderGoal.Performance => T("帧率优先已自动选用：", "Performance route: "),
                _ => hasAutomaticRoute ? T("该目标当前不可用。", "This goal is unavailable.") : T("画质优先与帧率优先不可用。", "Automatic routes are unavailable.")
            };
            summaryValue.Text = selection.Mode == 0 ? T("DLSS 5 神经渲染 · 模式一", "DLSS 5 neural · Mode 1") : selection.Mode.HasValue ? RouteName(selection.Mode) : hasAutomaticRoute
                ? T("请选择其他目标或自定义。", "Choose another goal or Custom.")
                : T("选择「自定义」可查看其他方案。", "Choose Custom for other routes.");
            int[] modes = [0, 4, 2];
            for (var i = 0; i < routes.Length; i++)
            {
                routes[i].Visible = custom;
                routes[i].Enabled = custom && selection.IsModeAvailable(modes[i]);
                routes[i].IsSelected = selection.Mode == modes[i];
                routes[i].Tail = !routes[i].Enabled ? T("不可用", "Unavailable") : routes[i].IsSelected ? T("✓ 已选", "✓ Selected") :
                    i == 0 ? T("画质方案", "Quality") : i == 1 ? T("帧率方案", "Performance") : T("可使用", "Available");
                routes[i].Invalidate();
            }
            listNote.Text = unsupported ? T("检测到的是核显或未支持型号；如另有 RX 独显，请检查系统显卡识别。", "If another RX GPU is installed, check GPU detection.") :
                T("手动选择后，游戏内实际效果仍需验证。", "Verify the selected route in game.");
            configure.Enabled = previewOnly || selection.CanConfigure;
            configure.Text = previewOnly ? T("关闭预览", "Close preview") : selection.CanConfigure ? T("一键配置 · ", "Configure · ") + RouteName(selection.Mode) :
                checking ? T("正在检测", "Checking") : hasAutomaticRoute ? T("请选择可用目标", "Choose an available goal") : T("当前无法开启 DLSS 5", "DLSS 5 unavailable");
            configure.Invalidate();
            LayoutPrototype();
        }
        finally { ResumeLayout(true); layingOut = false; }
    }

    void LayoutPrototype()
    {
        foreach (var (label, pixels, bold) in typography)
        {
            var wanted = pixels * ScaleFactor;
            if (Math.Abs(label.Font.Size - wanted) < .01f) continue;
            var previous = label.Font;
            label.Font = new Font("Microsoft YaHei", wanted, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            previous.Dispose();
        }
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        var width = Math.Min(860f, (area.Width - P(32)) / ScaleFactor);
        var inside = width - 58;
        var stack = inside < 560;
        var goalsY = 136f + (alert.Visible ? 46 : 0);
        var afterGoals = goalsY + (stack ? 251 : 77);
        var custom = selection.ShowSchemeList;
        var bodyHeight = checking || hasFailure ? 290 : 21 + afterGoals + (custom ? 261 : 76) + 16;
        var height = Math.Min(81 + bodyHeight + 67, (area.Height - P(32)) / ScaleFactor);
        ClientSize = new Size(P(width), P(height));
        Place(header, 1, 1, width - 2, 80);
        Place(logo, 24, 19, 42, 42);
        SetRoundedRegion(logo, P(10));
        Place(title, 83, 20, width - 155, 42);
        Place(close, width - 60, 20, 36, 40);
        Place(footer, 1, height - 67, width - 2, 66);
        Place(viewport, 1, 81, width - 2, height - 148);
        // Reserve the native scrollbar only when content exceeds the available height.
        if (P(bodyHeight) > viewport.Height) inside -= SystemInformation.VerticalScrollBarWidth / ScaleFactor;
        Place(content, 0, 0, inside + 56, bodyHeight);
        Place(waiting, 0, 0, width - 2, bodyHeight);
        Place(failed, 0, 0, width - 2, bodyHeight);
        Place(steps, 28, 21, inside, 15);
        Place(hardware, 28, 54, inside, 63);
        SetRoundedRegion(hardware, P(10));
        Place(gpu, 16, 13, inside - 210, 23);
        Place(gpuDetail, 16, 36, inside - 32, 16);
        Place(gpuStatus, inside - 198, 13, 182, 23);
        Place(alert, 30, 129, inside - 4, 32);
        Place(goalHeading, 28, goalsY - 26 + 21, inside, 16);
        for (var i = 0; i < goals.Length; i++)
        {
            var goalWidth = stack ? inside : (inside - 20) / 3;
            Place(goals[i], 28 + (stack ? 0 : i * (goalWidth + 10)), 21 + goalsY + (stack ? i * 87 : 0), goalWidth, 77);
        }
        var summaryY = 21 + afterGoals + 18;
        var prefixWidth = TextRenderer.MeasureText(summaryPrefix.Text, summaryPrefix.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width / ScaleFactor;
        Place(summaryPrefix, 31, summaryY, Math.Min(inside, prefixWidth), 58);
        Place(summaryValue, 31 + prefixWidth + 6, summaryY, Math.Max(0, inside - prefixWidth - 6), 58);
        var listY = 21 + afterGoals + 19;
        Place(listHeading, 28, listY, inside - 120, 18);
        Place(listHint, 28 + inside - 120, listY, 120, 18);
        for (var i = 0; i < routes.Length; i++) Place(routes[i], 28, listY + 20 + i * 66, inside, 66);
        Place(listNote, 28, listY + 229, inside, 20);
        Place(waitTitle, 28, 39, inside, 32);
        Place(waitStage, 28, 85, inside, 34);
        Place(waitElapsed, 28, 126, inside, 25);
        Place(progress, 28, 175, inside, 6);
        Place(failTitle, 28, 35, inside, 34);
        Place(failDetail, 28, 82, inside, 80);
        Place(retry, 28, 190, 130, 40);
        Place(manual, 170, 190, 175, 40);
        using var actionFont = new Font("Microsoft YaHei", 13 * ScaleFactor, FontStyle.Bold, GraphicsUnit.Pixel);
        var actionWidth = Math.Max(194, TextRenderer.MeasureText(configure.Text, actionFont).Width / ScaleFactor + 40);
        Place(configure, width - 30 - actionWidth, 13, actionWidth, 40);
        Place(back, width - 40 - actionWidth - 130, 13, 130, 40);
        SetRoundedRegion(this, P(18));
        if (Visible)
        {
            var center = Owner?.Bounds ?? area;
            Location = new Point(Math.Clamp(center.Left + (center.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(center.Top + (center.Height - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
    }

    static void SetRoundedRegion(Control control, float radius)
    {
        using var path = UiPaint.RoundPath(new RectangleF(0, 0, control.Width, control.Height), radius);
        var old = control.Region; control.Region = new Region(path); old?.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            clock.Dispose(); details.Dispose();
            foreach (var item in typography) item.Label.Font.Dispose();
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class SmartPrototypeLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var alignment = TextAlign switch
        {
            ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            alignment | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
            (AutoEllipsis ? TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis : TextFormatFlags.WordBreak));
    }
}

internal sealed class SmartPrototypeButton : Button
{
    public enum Style { Action, Goal, Route, Close }
    public Style PaintStyle { get; set; }
    public bool IsSelected { get; set; }
    public bool Primary { get; set; }
    public bool TopRule { get; set; }
    public string Detail { get; set; } = "";
    public string Tail { get; set; } = "";
    public SmartPrototypeButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.Opaque, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; Cursor = Cursors.Hand;
    }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => UiPaint.ParentBackground(this, e, InvokePaintBackground, InvokePaint);
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        int P(float v) => (int)Math.Round(v * scale);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color C(string hex) => ColorTranslator.FromHtml(hex);
        var ink = SmartRecommendationDialog.Ink;
        var muted = SmartRecommendationDialog.Muted;
        var acid = SmartRecommendationDialog.Accent;
        Color Tone(Color color) => Enabled ? color : Color.FromArgb(
            (int)(color.R * .54 + 7 * .46), (int)(color.G * .54 + 11 * .46), (int)(color.B * .54 + 16 * .46));
        void TextAt(string text, float size, bool bold, Color color, Rectangle rect, TextFormatFlags extra = TextFormatFlags.Left)
        {
            using var font = new Font("Microsoft YaHei", size * scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, text, font, rect, color,
                extra | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        if (PaintStyle == Style.Goal)
        {
            using var shape = UiPaint.RoundPath(new RectangleF(.5f, .5f, Width - 1, Height - 1), P(11));
            using var fill = new SolidBrush(Tone(IsSelected ? C("#142317") : SmartRecommendationDialog.Surface));
            using var border = new Pen(Tone(IsSelected ? acid : SmartRecommendationDialog.Rule), Math.Max(1, scale));
            g.FillPath(fill, shape); g.DrawPath(border, shape);
            TextAt(Text, 15, true, Tone(IsSelected ? acid : ink), new Rectangle(P(14), P(12), Width - P(28), P(21)));
            TextAt(Detail, 11, false, Tone(muted), new Rectangle(P(14), P(38), Width - P(28), P(22)));
        }
        else if (PaintStyle == Style.Route)
        {
            if (IsSelected) { using var fill = new SolidBrush(C("#101810")); g.FillRectangle(fill, ClientRectangle); }
            using var line = new Pen(SmartRecommendationDialog.Rule, Math.Max(1, scale));
            if (TopRule) g.DrawLine(line, 0, 0, Width, 0);
            g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            var centerY = Height / 2f;
            using var radio = new Pen(Tone(IsSelected ? acid : SmartRecommendationDialog.Dim), Math.Max(1, scale));
            g.DrawEllipse(radio, P(6), centerY - P(8.5f), P(17), P(17));
            if (IsSelected) { using var dot = new SolidBrush(acid); g.FillEllipse(dot, P(11), centerY - P(3.5f), P(7), P(7)); }
            TextAt(Text, 14, true, Tone(ink), new Rectangle(P(41), P(10), Width - P(145), P(21)));
            TextAt(Detail, 11, false, Tone(muted), new Rectangle(P(41), P(35), Width - P(145), P(18)));
            TextAt(Tail, 11, false, Tone(IsSelected ? acid : muted), new Rectangle(Width - P(100), 0, P(94), Height), TextFormatFlags.Right);
        }
        else if (PaintStyle == Style.Close)
            TextAt("×", 20, false, muted, ClientRectangle, TextFormatFlags.HorizontalCenter);
        else
        {
            using var shape = UiPaint.RoundPath(new RectangleF(0, 0, Width, Height), P(9));
            using var fill = new SolidBrush(!Enabled ? C("#293442") : Primary ? acid : C("#1d2935"));
            g.FillPath(fill, shape);
            TextAt(Text, 13, Primary, !Enabled ? muted : Primary ? C("#111902") : ink,
                new Rectangle(P(10), 0, Width - P(20), Height), TextFormatFlags.HorizontalCenter);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -P(4), -P(4)));
    }
}
