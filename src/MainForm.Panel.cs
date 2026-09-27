using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    void ShowLicenses()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var contents = new List<string>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Licenses.")))
        { using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); contents.Add(name + "\r\n\r\n" + reader.ReadToEnd()); }
        using var dialog = new Form { Text = "第三方组件与许可", Width = 760, Height = 600, StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.Join("\r\n\r\n", contents) }); dialog.ShowDialog(this);
    }
    ControlPanel? controlPanel;
    const int PanelHotkey = 0x4D55;
    bool panelHotkeyRegistered;
    System.Windows.Forms.Timer? panelFocusWatch;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam.ToInt32() == PanelHotkey && controlPanel is { IsDisposed: false } p)
        {
            if (p.Visible) p.Hide();
            else
            {
                try
                {
                    GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                    using var foreground = Process.GetProcessById((int)pid);
                    if (pid == Environment.ProcessId || string.Equals(foreground.MainModule?.FileName, p.GameExe, StringComparison.OrdinalIgnoreCase)) _ = ShowAuthorizedPanelAsync(p);
                }
                catch { }
            }
            return;
        }
        base.WndProc(ref m);
        // Preserve desktop resize behavior after introducing custom window chrome.
        if (m.Msg == 0x84 && m.Result == (IntPtr)1 && WindowState == FormWindowState.Normal)
        {
            var packed = m.LParam.ToInt64();
            var point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            var edge = Math.Max(5, (int)Math.Round(6 * DeviceDpi / 96d));
            bool left = point.X < edge, right = point.X >= ClientSize.Width - edge;
            bool top = point.Y < edge, bottom = point.Y >= ClientSize.Height - edge;
            m.Result = (IntPtr)(top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 1);
        }
    }
    async Task ShowAuthorizedPanelAsync(ControlPanel panel)
    {
        if (await RequireFeatureAsync("diagnostics.use") && !panel.IsDisposed) panel.Show();
    }
    async void OpenPanel()
    {
        if (selectedGame == null || busy) return;
        if (!await RequireFeatureAsync("diagnostics.use") || busy || selectedGame == null) return;
        if (controlPanel is { IsDisposed: false }) controlPanel.Close();
        var exe = selectedGame;
        controlPanel = new ControlPanel(exe, async () => await ExportDiagnosticForAsync(exe), InstallLivePanel, RequireFeatureAsync, BeginAccountTransaction);
        controlPanel.FormClosed += (_, _) => { panelFocusWatch?.Dispose(); panelFocusWatch = null; if (panelHotkeyRegistered) UnregisterHotKey(Handle, PanelHotkey); panelHotkeyRegistered = false; };
        bool native = File.Exists(Path.Combine(Path.GetDirectoryName(selectedGame)!, "AMD-DLSS-MU.addon64"));
        if (!native) panelHotkeyRegistered = RegisterHotKey(Handle, PanelHotkey, 0x4000, 0x77);
        controlPanel.HotkeyHint(native ? "F8 由游戏内原生面板处理；此窗口可手动打开。" : panelHotkeyRegistered ? "F8 显示/隐藏；需保持启动器运行，使用无边框/窗口模式。" : "F8 注册失败（可能被占用）；可使用此窗口。");
        controlPanel.Show();
        if (!native)
        {
            panelFocusWatch = new System.Windows.Forms.Timer { Interval = 300 };
            panelFocusWatch.Tick += (_, _) =>
            {
                bool relevant = false;
                try { GetWindowThreadProcessId(GetForegroundWindow(), out uint pid); using var p = Process.GetProcessById((int)pid); relevant = pid == Environment.ProcessId || string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); } catch { }
                if (!relevant && panelHotkeyRegistered) { UnregisterHotKey(Handle, PanelHotkey); panelHotkeyRegistered = false; }
                else if (relevant && !panelHotkeyRegistered) panelHotkeyRegistered = RegisterHotKey(Handle, PanelHotkey, 0x4000, 0x77);
            };
            panelFocusWatch.Start();
        }
    }
    async void InstallLivePanel(string exe)
    {
        try
        {
            if (busy) return;
            if (!await RequireFeatureAsync("configuration.edit") || busy) return;
            if (MessageBox.Show(this, "实验性实时面板：需要已安装的 ReShade 6.8.0 Add-on 版和指定 RenoDX v4.7。\n\n它不控制 AMD 模式一，也不自动安装这些运行时。连接后提供结构、色调、皮肤、样式等实时控件。\n\n安装到此游戏并纳入恢复记录？", "安装实时面板", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!await RequireFeatureAsync("configuration.edit") || busy) return;
            using var transaction = BeginAccountTransaction();
            LivePanel.Install(exe);
            panelFocusWatch?.Dispose(); panelFocusWatch = null;
            if (panelHotkeyRegistered) UnregisterHotKey(Handle, PanelHotkey); panelHotkeyRegistered = false;
            controlPanel?.HotkeyHint("原生面板已安装：启动游戏按 F8。需要 ReShade 成功加载插件。");
            status.Text = "实时面板已安装。连接状态和参数由游戏内运行时读回；未进行效果验证。";
            Diagnostics.Record(exe, "native-overlay", "installed"); UpdateButtons();
        }
        catch (Exception e) { Diagnostics.Record(exe, "native-overlay", "failed", e.ToString()); MessageBox.Show(this, e.Message, "面板安装未完成"); }
    }
}

public sealed class ControlPanel : Form
{
    public string GameExe { get; }
    readonly Label hint = new() { AutoSize = false, Height = 62, Dock = DockStyle.Top };
    readonly Label state = new() { AutoSize = false, Height = 130, Dock = DockStyle.Top };
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 2500 };
    readonly Func<string, Task<bool>> authorize;
    readonly Func<IDisposable> beginTransaction;
    bool refreshing;
    public void HotkeyHint(string value) => hint.Text = value;
    public ControlPanel(string exe, Func<Task> export, Action<string> installNative, Func<string, Task<bool>> authorize, Func<IDisposable> beginTransaction)
    {
        this.authorize = authorize; this.beginTransaction = beginTransaction;
        GameExe = exe; Text = "AMD DLSS MU · 游戏控制面板"; Size = new Size(500, 580); MinimumSize = new Size(440, 520);
        BackColor = Color.FromArgb(23, 29, 27); ForeColor = Color.FromArgb(229, 237, 230); Font = new Font("Microsoft YaHei UI", 10);
        TopMost = true; StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        var title = new Label { Text = Path.GetFileNameWithoutExtension(exe), Font = new Font(Font, FontStyle.Bold), Height = 36, Width = 425 };
        body.Controls.Add(title); hint.Width = state.Width = 425; body.Controls.Add(hint); body.Controls.Add(state);
        Button Add(string label, EventHandler action) { var b = new Button { Text = label, Width = 425, Height = 42, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 0, 0) }; b.Click += action; body.Controls.Add(b); return b; }
        Add("配置文件（保存后重新启动游戏）", (_, _) => EditConfig());
        Add("导出完整诊断（先预览）", async (_, _) => { try { await export(); } catch (Exception e) { MessageBox.Show(this, e.Message); } });
        Add("安装实时滑块插件 · RenoDX v4.7", (_, _) => installNative(GameExe));
        Add("打开游戏目录", async (_, _) => { try { if (await authorize("diagnostics.use")) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetDirectoryName(GameExe)! }, UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, e.Message); } });
        body.Controls.Add(new Label { Text = "原生实时菜单：模式一 End；OptiScaler Insert（默认）。\n本窗口不显示推测 FPS；“已加载”不代表神经渲染成功。", Width = 425, Height = 65, Margin = new Padding(0, 12, 0, 0) });
        Controls.Add(body);
        refresh.Tick += async (_, _) => await RefreshState(); Shown += async (_, _) => await RefreshState();
        VisibleChanged += (_, _) => refresh.Enabled = Visible;
        FormClosed += (_, _) => refresh.Dispose(); KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
    }
    async Task RefreshState()
    {
        if (refreshing) return; refreshing = true;
        try { var value = await Task.Run(() => GameManagement.Runtime(GameExe)); if (!IsDisposed) state.Text = value; }
        catch (Exception e) { if (!IsDisposed) state.Text = e.Message; }
        finally { refreshing = false; }
    }
    async void EditConfig()
    {
        try
        {
            if (!await authorize("configuration.edit") || IsDisposed) return;
            var dir = Path.GetDirectoryName(GameExe)!;
            var names = new[] { "dlssnr_on_amd.ini", "OptiScaler.ini" }.Where(n => File.Exists(Path.Combine(dir, n))).ToArray();
            if (names.Length == 0) throw new IOException("尚未找到配置文件，请先完成安装。");
            using var editor = new Form { Text = "配置编辑 · 退出游戏后才能保存", Size = new Size(780, 640), StartPosition = FormStartPosition.CenterParent };
            var select = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList }; select.Items.AddRange(names);
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, MaxLength = 1024 * 1024, WordWrap = false };
            var save = new Button { Text = "保存配置并保留备份（重启游戏生效）", Dock = DockStyle.Bottom, Height = 42 };
            string original = "", path = "";
            select.SelectedIndexChanged += (_, _) => { try { path = Path.Combine(dir, (string)select.SelectedItem!); Core.RejectLinks(path); if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("配置文件过大。"); original = File.ReadAllText(path); text.Text = original; } catch (Exception e) { save.Enabled = false; MessageBox.Show(editor, e.Message); } };
            save.Click += async (_, _) =>
            {
                try { if (!await authorize("configuration.edit") || editor.IsDisposed) return; using var transaction = beginTransaction(); LivePanel.SaveConfig(GameExe, path, original, text.Text); original = text.Text; MessageBox.Show(editor, "已保存。重新启动游戏后使用；此操作不是实时滑块。"); }
                catch (Exception e) { MessageBox.Show(editor, e.Message, "未保存"); }
            };
            editor.Controls.Add(text); editor.Controls.Add(select); editor.Controls.Add(save); select.SelectedIndex = 0; editor.ShowDialog(this);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
    }
}
