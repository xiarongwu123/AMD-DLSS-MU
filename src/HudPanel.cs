using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed class HudPanel : Panel
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
