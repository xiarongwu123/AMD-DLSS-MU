using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmdNrAssistant;

/// <summary>Card actions live on the cover; hovering and clicking artwork never imply installation.</summary>
public sealed class DlssCoverAction : Control
{
    public event EventHandler? ConfigureRequested;
    public event EventHandler? AdvancedRequested;
    public event EventHandler? RestoreRequested;
    bool loading;
    int progress;
    float reveal = 1f;
    readonly System.Windows.Forms.Timer revealTimer = new() { Interval = 16 };
    string primaryText = "开启 DLSS5";
    public bool English { get; set; }
    public bool Loading { get => loading; set { loading = value; Invalidate(); } }
    public int Progress { get => progress; set { progress = Math.Clamp(value, 0, 95); Invalidate(); } }
    public string PrimaryText { get => primaryText; set { primaryText = value; Invalidate(); } }

    public DlssCoverAction()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Default;
        TabStop = true;
        revealTimer.Tick += (_, _) =>
        {
            reveal = Math.Min(1f, reveal + .14f);
            Invalidate();
            if (reveal >= 1f) revealTimer.Stop();
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        revealTimer.Stop();
        if (Visible && !Loading) { reveal = 0f; revealTimer.Start(); }
        else reveal = 1f;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) revealTimer.Dispose();
        base.Dispose(disposing);
    }

    Rectangle Primary => new(Math.Max(14, Width / 9), Height / 2 - 30,
        Width - 2 * Math.Max(14, Width / 9), 54);
    Rectangle Advanced => new(Primary.Left, Primary.Bottom + 12, (Primary.Width - 10) / 2, 38);
    Rectangle Restore => new(Advanced.Right + 10, Advanced.Top, Advanced.Width, 38);

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || Loading) return;
        if (Primary.Contains(e.Location)) ConfigureRequested?.Invoke(this, EventArgs.Empty);
        else if (Advanced.Contains(e.Location)) AdvancedRequested?.Invoke(this, EventArgs.Empty);
        else if (Restore.Contains(e.Location)) RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Loading || e.KeyCode is not Keys.Enter and not Keys.Space) return;
        ConfigureRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = Primary.Contains(e.Location) || Advanced.Contains(e.Location) || Restore.Contains(e.Location)
            ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
        => UiPaint.ParentBackground(this, e, InvokePaintBackground, InvokePaint);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width < 2 || Height < 2) return;
        using var coverPath = UiPaint.RoundPath(new RectangleF(.5f, .5f, Width - 1, Height - 1),
            (Parent is CoverPictureBox cover ? cover.CornerRadius : 16) * DeviceDpi / 96f, topOnly: true);
        using var shade = new SolidBrush(Color.FromArgb((int)(219 * reveal), MainForm.Base));
        g.FillPath(shade, coverPath);
        if (reveal < .55f && !Loading) return;
        if (Loading)
        {
            var track = new Rectangle(Math.Max(18, Width / 9), Height / 2 + 12,
                Width - 2 * Math.Max(18, Width / 9), 8);
            using var trackBrush = new SolidBrush(MainForm.Line);
            using var fillBrush = new SolidBrush(MainForm.Acid);
            g.FillRectangle(trackBrush, track);
            g.FillRectangle(fillBrush, new Rectangle(track.Left, track.Top,
                Math.Max(4, track.Width * Progress / 100), track.Height));
            using var font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, English ? $"Configuring · {Progress}%" : $"正在配置 · {Progress}%", font,
                new Rectangle(0, track.Top - 46, Width, 34), MainForm.Ink,
                UiPaint.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        DrawButton(g, Primary, MainForm.Acid, MainForm.OnAccent, PrimaryText, true);
        DrawButton(g, Advanced, MainForm.Surface, MainForm.Ink, English ? "Advanced" : "高级选项", false);
        DrawButton(g, Restore, MainForm.Surface, MainForm.Ink, English ? "Restore" : "恢复配置", false);
    }

    static void DrawButton(Graphics g, Rectangle bounds, Color fill, Color ink, string label, bool primary)
    {
        using var path = new GraphicsPath();
        var d = primary ? 22 : 16;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(primary ? MainForm.Acid : MainForm.Line);
        g.DrawPath(pen, path);
        using var font = new Font("Microsoft YaHei UI", primary ? 11f : 9f, primary ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, label, font, bounds, ink,
            UiPaint.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
