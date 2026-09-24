using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmdNrAssistant;

/// <summary>Interactive cover overlay; painting never changes layout or the game state.</summary>
public sealed class DlssCoverAction : Button
{
    public DlssCoverAction()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Text = "开启 DLSS5";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(MainForm.Base);
        if (Parent is PictureBox { Image: { } image })
        {
            var scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
            var w = image.Width * scale; var h = image.Height * scale;
            g.DrawImage(image, (Width - w) / 2, 0, w, h);
        }
        using var shade = new SolidBrush(Color.FromArgb(220, MainForm.Base));
        g.FillRectangle(shade, ClientRectangle);
        using var edge = new Pen(MainForm.Acid, Math.Max(1, DeviceDpi / 96f));
        g.DrawRectangle(edge, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        var button = new Rectangle(Width / 8, Height / 2 - 25, Width * 3 / 4, 50);
        using var fill = new SolidBrush(MainForm.Acid);
        g.FillRectangle(fill, button);
        using var labelFont = new Font(Font.FontFamily, 11, FontStyle.Bold);
        TextRenderer.DrawText(g, Text, labelFont, button, MainForm.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var mono = new Font("Consolas", 8);
        TextRenderer.DrawText(g, "MU / GRAPHICS", mono, new Rectangle(0, button.Top - 36, Width, 24), MainForm.Acid, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "ENTER PERFORMANCE MODE", mono, new Rectangle(0, button.Bottom + 20, Width, 26), MainForm.Muted, TextFormatFlags.HorizontalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(button, -4, -4), MainForm.OnAccent, MainForm.Acid);
    }
}
