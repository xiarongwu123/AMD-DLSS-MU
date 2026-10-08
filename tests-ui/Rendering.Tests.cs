using System.Reflection;
using AmdNrAssistant;

// Run on Windows: dotnet run --project tests-ui/rendering/Rendering.Tests.csproj -c Release
// Exercises HWND rendering and the actual ButtonBase style, not mocked painting.
internal static class RenderingTests
{
    static readonly MethodInfo GetStyle = typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo Enter = typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo Leave = typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { ClientSize = new Size(600, 300), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        using var parent = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 29, 41) };
        form.Controls.Add(parent);
        using var action = new RoundedButton { Location = new Point(23, 37), Size = new Size(210, 52),
            BackColor = Color.FromArgb(101, 170, 40), ForeColor = Color.White, Radius = 18 };
        using var tab = new UnderlineTabButton { Location = new Point(253, 109), Size = new Size(210, 52) };
        parent.Controls.Add(action); parent.Controls.Add(tab);
        form.Show();
        // Construct the actual dialog icons: setting Transparent before enabling
        // SupportsTransparentBackColor throws on Windows before the dialog can render.
        var iconType = typeof(MainForm).Assembly.GetType("AmdNrAssistant.SmartLineIcon", throwOnError: true)!;
        foreach (var kind in new[] { "chip", "display", "target", "image", "bars", "sliders", "gear", "file", "folder", "shield", "puzzle" })
        {
            using var icon = (Control)Activator.CreateInstance(iconType, kind, Color.LightGreen)!;
            icon.SetBounds(15, 220, 48, 48);
            parent.Controls.Add(icon);
            if (!(bool)GetStyle.Invoke(icon, new object[] { ControlStyles.SupportsTransparentBackColor })!)
                throw new Exception("SmartLineIcon does not support its transparent background");
            using var rendered = Capture(icon);
            parent.Controls.Remove(icon);
        }
        Console.WriteLine("PASS SmartLineIcon construction and drawing for every dialog icon");
        var closeType = typeof(MainForm).Assembly.GetType("AmdNrAssistant.SmartCloseButton", throwOnError: true)!;
        using (var close = (Button)Activator.CreateInstance(closeType)!)
        {
            close.Location = new Point(80, 220);
            close.BackColor = parent.BackColor;
            close.ForeColor = Color.White;
            parent.Controls.Add(close);
            foreach (var size in new[] { 38, 48, 57 })
            {
                close.Size = new Size(size, size);
                using var rendered = Capture(close);
                if (!string.IsNullOrEmpty(close.Text))
                    throw new Exception("Close icon must not pass through text ellipsis rendering");
                var marks = 0;
                for (var y = size / 4; y < size * 3 / 4; y++)
                    for (var x = size / 4; x < size * 3 / 4; x++)
                        if (rendered.GetPixel(x, y).GetBrightness() > .8f) marks++;
                if (marks == 0) throw new Exception("Close icon was not drawn");
            }
            var clicked = false;
            close.Click += (_, _) => clicked = true;
            close.PerformClick();
            if (!clicked) throw new Exception("Close button did not dispatch its action");
            parent.Controls.Remove(close);
        }
        Console.WriteLine("PASS Close button rendering at three sizes and click");
        foreach (var control in new Control[] { action, tab })
        {
            if ((bool)GetStyle.Invoke(control, new object[] { ControlStyles.Opaque })!)
                throw new Exception(control.GetType().Name + " skips background painting (Opaque)");
            control.Text = "";
            using var before = Capture(control);
            if (before.GetPixel(0, 0).ToArgb() != parent.BackColor.ToArgb())
                throw new Exception(control.GetType().Name + " has a foreign corner background");
            for (var i = 0; i < 12; i++)
            {
                control.Text = i % 2 == 0 ? "重新扫描 OLD TEXT" : "注册／下载任务";
                Enter.Invoke(control, new object[] { EventArgs.Empty });
                Pump(60);
                using var dirty = Capture(control);
                Leave.Invoke(control, new object[] { EventArgs.Empty });
            }
            control.Text = "";
            Pump(600);
            using var after = Capture(control);
            for (var y = 0; y < before.Height; y++)
                for (var x = 0; x < before.Width; x++)
                    if (before.GetPixel(x, y) != after.GetPixel(x, y))
                        throw new Exception($"{control.GetType().Name}: stale hover/text pixel at {x},{y}");
            Console.WriteLine("PASS " + control.GetType().Name + " background and repeated hover/text repaint");
        }
        form.Close();
    }

    static Bitmap Capture(Control control)
    {
        var image = new Bitmap(control.Width, control.Height);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.Magenta);
        control.DrawToBitmap(image, control.ClientRectangle);
        return image;
    }

    static void Pump(int milliseconds)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
