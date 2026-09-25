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
        var targetAspect = Width / (float)Height;
        var imageAspect = artwork.Width / (float)artwork.Height;
        var source = targetAspect > imageAspect
            ? new RectangleF(0, (artwork.Height - artwork.Width / targetAspect) * FocusY,
                artwork.Width, artwork.Width / targetAspect)
            : new RectangleF((artwork.Width - artwork.Height * targetAspect) * FocusX, 0,
                artwork.Height * targetAspect, artwork.Height);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(artwork, ClientRectangle, source, GraphicsUnit.Pixel);
        using var border = new Pen(BorderColor == default ? MainForm.Line : BorderColor, 2f)
        { Alignment = PenAlignment.Inset };
        using var path = new GraphicsPath();
        var diameter = Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1));
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        g.DrawPath(border, path);
    }
}
