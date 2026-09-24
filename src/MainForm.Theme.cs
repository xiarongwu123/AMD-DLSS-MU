using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    sealed class UiPreferences
    {
        public bool AutoCheckUpdates { get; set; } = true;
        public string Theme { get; set; } = "dark";
    }

    static bool globalDark = true;
    bool darkMode = true;
    internal static Color Base => globalDark ? Color.FromArgb(5, 8, 13) : Color.FromArgb(239, 242, 246);
    internal static Color Surface => globalDark ? Color.FromArgb(11, 17, 26) : Color.White;
    internal static Color Sidebar => globalDark ? Color.FromArgb(19, 26, 36) : Color.FromArgb(248, 250, 252);
    internal static Color Line => globalDark ? Color.FromArgb(35, 48, 65) : Color.FromArgb(210, 218, 228);
    internal static Color Acid => globalDark ? Color.FromArgb(173, 255, 24) : Color.FromArgb(62, 128, 15);
    internal static Color SelectedSurface => globalDark ? Color.FromArgb(21, 33, 25) : Color.FromArgb(230, 241, 220);
    internal static Color Ink => globalDark ? Color.FromArgb(242, 247, 251) : Color.FromArgb(28, 39, 53);
    internal static Color Muted => globalDark ? Color.FromArgb(148, 161, 175) : Color.FromArgb(87, 102, 120);
    internal static Color OnAccent => globalDark ? Color.FromArgb(8, 16, 7) : Color.FromArgb(248, 250, 245);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    void DragWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    void LoadPreferences()
    {
        try
        {
            if (File.Exists(PreferencesFile))
            {
                var saved = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PreferencesFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (saved != null)
                {
                    autoCheckUpdates = saved.AutoCheckUpdates;
                    darkMode = !saved.Theme.Equals("light", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { /* Invalid preferences fall back to safe defaults. */ }
        globalDark = darkMode;
    }

    void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesFile)!);
        var json = JsonSerializer.Serialize(new UiPreferences
        {
            AutoCheckUpdates = autoCheckUpdates,
            Theme = darkMode ? "dark" : "light"
        }, new JsonSerializerOptions { WriteIndented = true });
        var temporary = PreferencesFile + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, PreferencesFile, true);
    }

    void ToggleTheme() => ApplyTheme(!darkMode, true);

    void ApplyTheme(bool useDark, bool persist)
    {
        var oldBase = Base; var oldSurface = Surface; var oldSidebar = Sidebar;
        var oldLine = Line; var oldAcid = Acid; var oldSelected = SelectedSurface;
        var oldInk = Ink; var oldMuted = Muted; var oldOnAccent = OnAccent;
        darkMode = useDark; globalDark = useDark;
        Color Map(Color value)
        {
            if (value == oldBase) return Base;
            if (value == oldSurface) return Surface;
            if (value == oldSidebar) return Sidebar;
            if (value == oldLine) return Line;
            if (value == oldAcid) return Acid;
            if (value == oldSelected) return SelectedSurface;
            if (value == oldInk) return Ink;
            if (value == oldMuted) return Muted;
            if (value == oldOnAccent) return OnAccent;
            return value;
        }
        void Recolor(Control control)
        {
            if (control.BackColor != Color.Transparent && control.BackColor != Color.Empty)
                control.BackColor = Map(control.BackColor);
            if (control.ForeColor != Color.Empty) control.ForeColor = Map(control.ForeColor);
            if (control is RoundedPanel rounded && rounded.BorderColor != default)
                rounded.BorderColor = Map(rounded.BorderColor);
            foreach (Control child in control.Controls) Recolor(child);
            ApplyNativeTheme(control);
            control.Invalidate(true);
        }
        BackColor = Line; ForeColor = Ink; Recolor(this);
        if (configurationSurface != null && configurationSurface.Parent == null) Recolor(configurationSurface);
        foreach (var button in navigation)
        {
            bool current = navigationPages[navigation.IndexOf(button)] == activePage;
            button.BackColor = current ? SelectedSurface : Sidebar;
            button.ForeColor = current ? Acid : Muted;
        }
        UpdateThemeToggleText();
        themeToggle.Glyph = darkMode ? "\uE706" : "\uE708";
        SetDarkTitleBar();
        if (persist)
        {
            try { SavePreferences(); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "主题设置未保存"); }
        }
    }

    void UpdateThemeToggleText()
    {
        themeToggle.Text = english
            ? (darkMode ? "Light theme" : "Dark theme")
            : (darkMode ? "切换浅色" : "切换深色");
    }

    void ApplyNativeTheme(Control control)
    {
        if (!OperatingSystem.IsWindows() || !control.IsHandleCreated) return;
        try { SetWindowTheme(control.Handle, darkMode ? "DarkMode_Explorer" : "Explorer", null); }
        catch { /* Older Windows builds may not expose the dark Explorer theme. */ }
    }

    void SetDarkTitleBar()
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int enabled = darkMode ? 1 : 0;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        int cornerPreference = 2;
        DwmSetWindowAttribute(Handle, 33, ref cornerPreference, sizeof(int));
    }

    void ThemeWindow(Form form)
    {
        form.BackColor = Surface; form.ForeColor = Ink;
        form.HandleCreated += (_, _) =>
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                int enabled = darkMode ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            ApplyNativeTheme(form);
        };
    }
}
