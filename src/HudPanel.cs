using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public class HudPanel : Panel
{
    public HudPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(MainForm.Base);
        var step = Math.Max(1, (int)(64 * DeviceDpi / 96f));
        using var grid = new Pen(Color.FromArgb(80, MainForm.Line));
        for (var x = 0; x < Width; x += step) e.Graphics.DrawLine(grid, x, 0, x, Height);
        for (var y = 0; y < Height; y += step) e.Graphics.DrawLine(grid, 0, y, Width, y);
        if (Width < 2 || Height < 2) return;
        var area = new Rectangle(Width / 2, 0, Width / 2, Math.Min(Height, 500));
        using var glow = new LinearGradientBrush(area, Color.FromArgb(14, MainForm.Acid), Color.Transparent, 110);
        e.Graphics.FillRectangle(glow, area);
    }
}

public sealed class PremiumHeaderPanel : Panel
{
    public PremiumHeaderPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(MainForm.Base);
        if (Width > 1 && Height > 1)
        {
            using var tint = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(0, MainForm.Acid), Color.FromArgb(19, MainForm.Acid), 0f);
            e.Graphics.FillRectangle(tint, ClientRectangle);
            using var line = new Pen(Color.FromArgb(95, MainForm.Line));
            e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }
    }
}

public sealed class AmbientCanvasPanel : Panel
{
    public AmbientCanvasPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(MainForm.Base);
        if (Width < 2 || Height < 2) return;
        var top = MainForm.IsDarkTheme ? Color.FromArgb(8, 24, 25) : Color.FromArgb(229, 240, 230);
        using var atmosphere = new LinearGradientBrush(ClientRectangle, top, MainForm.Base, 90f);
        e.Graphics.FillRectangle(atmosphere, ClientRectangle);
        // A radial glow fades on every edge; a rectangular alpha fill left a
        // visible green block behind the cards on high-DPI Windows desktops.
        using var haloPath = new GraphicsPath();
        haloPath.AddEllipse(Width * .42f, -Height * .26f, Width * .8f, Height * .95f);
        using var halo = new PathGradientBrush(haloPath)
        {
            CenterColor = Color.FromArgb(MainForm.IsDarkTheme ? 43 : 23, MainForm.Acid),
            SurroundColors = new[] { Color.FromArgb(0, MainForm.Acid) }
        };
        e.Graphics.FillPath(halo, haloPath);
    }
}

public sealed class HeroTitleControl : Control
{
    public HeroTitleControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        using var light = new SolidBrush(MainForm.Ink);
        using var accent = new LinearGradientBrush(ClientRectangle, MainForm.Acid,
            Color.FromArgb(75, 225, 124), 0f);
        const string prefix = "一键配置 AMD";
        e.Graphics.DrawString(prefix, Font, light, 0f, 0f, format);
        var prefixWidth = e.Graphics.MeasureString(prefix, Font, int.MaxValue, format).Width;
        e.Graphics.DrawString("DLSS5", Font, accent, prefixWidth + 12f * DeviceDpi / 96f, 0f, format);
    }
}

// The slot stays in the flow layout while its content rises on hover, so the
// animation never makes neighbouring cards jump or changes hit-test order.
public sealed class HoverLiftSlot : Panel
{
    readonly System.Windows.Forms.Timer motion = new() { Interval = 16 };
    Control? content;
    float lift;
    bool hot;
    public Control? Content
    {
        get => content;
        set
        {
            if (content != null) Controls.Remove(content);
            content = value;
            if (value == null) return;
            Controls.Add(value);
            Track(value);
            LayoutContent();
        }
    }

    public HoverLiftSlot()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        motion.Tick += (_, _) =>
        {
            var target = hot ? 6f * DeviceDpi / 96f : 0f;
            lift += (target - lift) * .27f;
            if (Math.Abs(target - lift) < .2f) { lift = target; motion.Stop(); }
            LayoutContent();
            Invalidate();
        };
    }

    public void Track(Control control)
    {
        control.MouseEnter += (_, _) => SetHot(true);
        control.MouseLeave += (_, _) =>
        {
            if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) SetHot(false);
        };
        foreach (Control child in control.Controls) Track(child);
    }

    void SetHot(bool value)
    {
        if (hot == value) return;
        hot = value;
        if (content is RoundedPanel card) card.Hovered = value;
        motion.Start();
    }

    void LayoutContent()
    {
        if (content == null || Width < 1 || Height < 1) return;
        var top = Math.Max(0, (int)Math.Round(7 * DeviceDpi / 96f - lift));
        content.SetBounds(0, top, Width, Math.Max(1, Height - (int)Math.Round(9 * DeviceDpi / 96f)));
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutContent(); }
    protected override void Dispose(bool disposing) { if (disposing) motion.Dispose(); base.Dispose(disposing); }
}

public sealed class StatusChip : Control
{
    public StatusChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("Microsoft YaHei UI", 9f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = new GraphicsPath();
        var d = Math.Min(20, Height - 1);
        shape.AddArc(0, 0, d, d, 180, 90);
        shape.AddArc(Width - d - 1, 0, d, d, 270, 90);
        shape.AddArc(Width - d - 1, Height - d - 1, d, d, 0, 90);
        shape.AddArc(0, Height - d - 1, d, d, 90, 90);
        shape.CloseFigure();
        using var fill = new SolidBrush(MainForm.Sidebar);
        using var border = new Pen(MainForm.Line, 1f);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
        using var iconFont = new Font("Segoe MDL2 Assets", 10f);
        TextRenderer.DrawText(e.Graphics, "\uE721", iconFont, new Rectangle(7, 0, 23, Height),
            MainForm.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(34, 0, Math.Max(1, Width - 39), Height),
            MainForm.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

public sealed class LibraryScrollPanel : HudPanel
{
    public int LibraryStart { get; set; }
    public bool LockToLibrary { get; set; }
    public event EventHandler? EnterLibraryRequested;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!LockToLibrary && LibraryStart > 0 && e.Delta < 0)
        {
            EnterLibraryRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (LockToLibrary && e.Delta > 0 && -AutoScrollPosition.Y <= LibraryStart + 1)
            return;
        base.OnMouseWheel(e);
        ClampToLibrary();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        if (LockToLibrary && IsHandleCreated && se.ScrollOrientation == ScrollOrientation.VerticalScroll)
            BeginInvoke((Action)ClampToLibrary);
    }

    void ClampToLibrary()
    {
        if (!LockToLibrary || LibraryStart <= 0 || -AutoScrollPosition.Y >= LibraryStart) return;
        AutoScrollPosition = new Point(0, LibraryStart);
    }
}

// Real artwork is embedded and cropped to fill; live controls remain native.
public sealed class ArtworkPanel : RoundedPanel
{
    readonly Image artwork;
    public float FocusX { get; set; } = .5f;
    public float FocusY { get; set; } = .5f;
    public bool ShowBorder { get; set; } = true;
    public bool FadeBottom { get; set; }

    public ArtworkPanel(string resourceName)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("缺少内置背景素材：" + resourceName);
        using var decoded = Image.FromStream(stream);
        artwork = new Bitmap(decoded);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) artwork.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var source = ArtworkSource();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(artwork, ClientRectangle, source, GraphicsUnit.Pixel);
        if (FadeBottom)
        {
            var fade = new Rectangle(0, Math.Max(0, Height * 2 / 3), Width, Math.Max(1, Height / 3));
            using var bottom = new LinearGradientBrush(fade, Color.Transparent, MainForm.Base, 90f);
            g.FillRectangle(bottom, fade);
        }
        if (!ShowBorder) return;
        using var border = new Pen(BorderColor == default ? Color.FromArgb(80, MainForm.Acid) : BorderColor, 1.4f)
        { Alignment = PenAlignment.Inset };
        using var path = new GraphicsPath();
        var diameter = Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1));
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        g.DrawPath(border, path);
        using var softEdge = new Pen(Color.FromArgb(23, MainForm.Acid), 5f) { Alignment = PenAlignment.Inset };
        g.DrawPath(softEdge, path);
    }

    RectangleF ArtworkSource()
    {
        var targetAspect = Width / (float)Math.Max(1, Height);
        var imageAspect = artwork.Width / (float)artwork.Height;
        return targetAspect > imageAspect
            ? new RectangleF(0, (artwork.Height - artwork.Width / targetAspect) * FocusY,
                artwork.Width, artwork.Width / targetAspect)
            : new RectangleF((artwork.Width - artwork.Height * targetAspect) * FocusX, 0,
                artwork.Height * targetAspect, artwork.Height);
    }

    public void DrawArtworkSection(Graphics graphics, Rectangle section)
    {
        if (Width < 1 || Height < 1 || section.Width < 1 || section.Height < 1) return;
        var full = ArtworkSource();
        var source = new RectangleF(full.Left + full.Width * section.Left / Width,
            full.Top + full.Height * section.Top / Height,
            full.Width * section.Width / Width, full.Height * section.Height / Height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(artwork, new Rectangle(0, 0, section.Width, section.Height), source, GraphicsUnit.Pixel);
    }
}

public sealed class GlassPanel : RoundedPanel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent is ArtworkPanel art) art.DrawArtworkSection(e.Graphics, Bounds);
        else e.Graphics.Clear(MainForm.Base);
        if (Width < 2 || Height < 2) return;
        using var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1)));
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(Width - d - 1, Height - d - 1, d, d, 0, 90);
        path.AddArc(0, Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        using var tint = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(231, MainForm.Surface), Color.FromArgb(205, MainForm.SelectedSurface), 15f);
        e.Graphics.FillPath(tint, path);
        using var outer = new Pen(Color.FromArgb(85, MainForm.Acid), 1.5f) { Alignment = PenAlignment.Inset };
        e.Graphics.DrawPath(outer, path);
    }
}

public sealed class GlowPanel : RoundedPanel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(UiPaint.OpaqueBackground(Parent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1)));
        path.AddArc(1, 1, d, d, 180, 90);
        path.AddArc(Width - d - 2, 1, d, d, 270, 90);
        path.AddArc(Width - d - 2, Height - d - 2, d, d, 0, 90);
        path.AddArc(1, Height - d - 2, d, d, 90, 90);
        path.CloseFigure();
        using var fill = new LinearGradientBrush(ClientRectangle, MainForm.SelectedSurface, MainForm.Surface, 10f);
        e.Graphics.FillPath(fill, path);
        using var edge = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(130, MainForm.Acid), Color.FromArgb(35, MainForm.Acid), 0f);
        using var border = new Pen(edge, 1.5f) { Alignment = PenAlignment.Inset };
        e.Graphics.DrawPath(border, path);
    }
}
