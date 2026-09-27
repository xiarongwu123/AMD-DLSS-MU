using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmdNrAssistant;

/// <summary>A quiet, keyboard-accessible scroll cue, not a filled action button.</summary>
public sealed class ScrollHintControl : Control
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 32 };
    float phase;
    bool hover;
    public ScrollHintControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable, true);
        BackColor = Color.Transparent; Cursor = Cursors.Hand; TabStop = true;
        AccessibleRole = AccessibleRole.Link; AccessibleName = "下滑查看全部游戏";
        animation.Tick += (_, _) => { phase += .13f; Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && SystemInformation.IsMenuAnimationEnabled) animation.Start(); else animation.Stop();
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var color = hover || Focused ? MainForm.Acid : ForeColor;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height - (int)(14 * scale)),
            color, UiPaint.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var x = Width / 2f; var y = Height - 10 * scale + (float)Math.Sin(phase) * 2 * scale;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLines(pen, new[] { new PointF(x - 4 * scale, y - 2 * scale), new PointF(x, y + 2 * scale), new PointF(x + 4 * scale, y - 2 * scale) });
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
