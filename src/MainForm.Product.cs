using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel downloadsPage = new() { Dock = DockStyle.Fill, Padding = new Padding(28), AutoScroll = true };
    readonly Panel helpPage = new() { Dock = DockStyle.Fill, Padding = new Padding(28), AutoScroll = true };
    readonly Panel settingsPage = new() { Dock = DockStyle.Fill, Padding = new Padding(28), AutoScroll = true };
    readonly FlowLayoutPanel downloadList = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly List<DownloadRow> downloadRows = new();
    RoundedPanel? productToast;
    System.Windows.Forms.Timer? productToastTimer;
    bool showCompleted, autoCheckUpdates = true;
    bool optiVulkan;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetDarkTitleBar();
        BeginInvoke((Action)(() => ApplyTheme(darkMode, false)));
    }
    static string PreferencesFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "ui-preferences.json");

    sealed class DownloadRow
    {
        public required DownloadSession Session;
        public required RoundedPanel Card;
        public required Label Info;
        public required AccentProgressBar Bar;
        public required Button Pause;
        public required Button Cancel;
        public required Button Retry;
        public required string Title;
        public string State = "等待下载";
        public int Percent;
        public long Size;
        public string Detail = "";
    }
    void BuildProductPages()
    {
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); title.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        title.Controls.Add(PageTitle("下载任务", "Downloads"), 0, 0);
        title.Controls.Add(new Label { Text = "集中查看组件下载进度与校验结果。", Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 1);
        rows.Controls.Add(title, 0, 0);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var activeTab = Localize(new UnderlineTabButton { Selected = true, Size = new Size(144, 40) }, "进行中 / 未完成", "Active / incomplete");
        var doneTab = Localize(new UnderlineTabButton { Size = new Size(110, 40) }, "已完成", "Completed");
        activeTab.Click += (_, _) => { showCompleted = false; activeTab.Selected = true; doneTab.Selected = false; RenderDownloads(); };
        doneTab.Click += (_, _) => { showCompleted = true; activeTab.Selected = false; doneTab.Selected = true; RenderDownloads(); };
        tabs.Controls.Add(activeTab); tabs.Controls.Add(doneTab);
        rows.Controls.Add(tabs, 0, 1); rows.Controls.Add(downloadList, 0, 2);
        downloadsPage.Controls.Add(rows);
        downloadList.ClientSizeChanged += (_, _) => { foreach (var item in downloadRows) item.Card.Width = Math.Max(280, downloadList.ClientSize.Width - 28); };
        BuildHelp(); BuildSettings();
        pageHost.Controls.Add(downloadsPage); pageHost.Controls.Add(helpPage); pageHost.Controls.Add(settingsPage);
        RenderDownloads();
    }
    FlowLayoutPanel PageStack(Panel page)
    {
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Location = new Point(page.Padding.Left, page.Padding.Top) };
        page.Controls.Add(stack);
        void Fit() { var width = Math.Max(280, page.ClientSize.Width - page.Padding.Horizontal - 24); stack.MaximumSize = new Size(width, 0); stack.Width = width; foreach (Control c in stack.Controls) if (c is Label) c.MaximumSize = new Size(width, 0); }
        page.ClientSizeChanged += (_, _) => Fit(); Fit(); return stack;
    }
    void Paragraph(FlowLayoutPanel stack, string text)
        => stack.Controls.Add(new Label { AutoSize = true, Text = text, ForeColor = muted, Margin = new Padding(0, 8, 0, 22), MaximumSize = new Size(680, 0) });
    void BuildHelp()
    {
        var stack = PageStack(helpPage); stack.Controls.Add(Heading("帮助与反馈", "Help & feedback", 23));
        Paragraph(stack, "先定位问题，不必反复重装。");
        stack.Controls.Add(Heading("End 菜单没有反应？", "No End menu?", 14));
        Paragraph(stack, "模式一：确认游戏内开启 FSR 超分辨率，并进入实际场景。模式二使用 Insert 菜单。组件存在不代表运行时已生效。");
        stack.Controls.Add(Action("查看诊断", "Diagnostics", (_, _) => ShowDiagnosticsMenu()));
        stack.Controls.Add(Heading("游戏卡顿或闪退？", "Stuttering or crashes?", 14));
        Paragraph(stack, "退出游戏后，在游戏库悬停对应游戏封面并点击“恢复配置”，再验证原始状态。带反作弊的联网游戏请勿贸然安装图形插件。");
        stack.Controls.Add(Action("返回游戏库", "Game library", async (_, _) => await NavigateAuthorizedAsync(1)));
        stack.Controls.Add(Heading("联系作者", "Contact", 14));
        stack.Controls.Add(Action("加入讨论组", "Community", async (_, _) => await NavigateAuthorizedAsync(2)));
    }
    void BuildSettings()
    {
        var stack = PageStack(settingsPage); stack.Controls.Add(Heading("设置", "Settings", 23));
        Paragraph(stack, "保持简单，把时间留给游戏。");
        stack.Controls.Add(Heading("外观", "Appearance"));
        var themes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 18) };
        themes.Controls.Add(Action("夜间模式", "Dark", async (_, _) => { if (await RequireFeatureAsync("configuration.edit")) ApplyTheme(true, true); }));
        themes.Controls.Add(Action("日间模式", "Light", async (_, _) => { if (await RequireFeatureAsync("configuration.edit")) ApplyTheme(false, true); }));
        stack.Controls.Add(themes);
        stack.Controls.Add(Action("中文 / EN", "EN / 中文", async (_, _) => { if (await RequireFeatureAsync("configuration.edit")) { english = !english; ApplyLanguage(english); ApplyAccountGate(); } }));
        var auto = new CheckBox { Text = "启动时自动检查更新", Checked = autoCheckUpdates, AutoSize = true, Margin = new Padding(0, 28, 0, 12) };
        bool changingAuto = false;
        auto.CheckedChanged += async (_, _) =>
        {
            if (changingAuto) return; changingAuto = true; auto.Enabled = false;
            try { if (await RequireFeatureAsync("configuration.edit")) { autoCheckUpdates = auto.Checked; SavePreferences(); } else auto.Checked = autoCheckUpdates; }
            catch (Exception e) { MessageBox.Show(this, e.Message, "设置未保存"); }
            finally { changingAuto = false; auto.Enabled = true; }
        };
        stack.Controls.Add(auto);
        stack.Controls.Add(Heading("下载源", "Download sources"));
        Paragraph(stack, "自动选择与切换，无需手动填写镜像。");
        stack.Controls.Add(Heading("版本", "Version")); Paragraph(stack, "AMD DLSS MU v" + AutoUpdate.DisplayVersion);
        stack.Controls.Add(Action("检查更新", "Check updates", async (_, _) => await CheckAppUpdateAsync(false)));
    }
    async void ShowAdvanced()
    {
        if (busy) return;
        if (!await RequireFeatureAsync("configuration.edit") || busy) return;
        using var dialog = ProductDialog("高级选项", 530, 340);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(Heading("安装方案", "Installation mode", 16));
        var mode = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Surface, ForeColor = ink };
        mode.Items.AddRange(installMode.Items.Cast<object>().ToArray()); mode.SelectedIndex = installMode.SelectedIndex; stack.Controls.Add(mode);
        var api = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Surface, ForeColor = ink };
        api.Items.AddRange(new[] { "OptiScaler · DirectX 11 / 12（默认）", "OptiScaler · Vulkan" }); api.SelectedIndex = optiVulkan ? 1 : 0; api.Enabled = mode.SelectedIndex == 1;
        mode.SelectedIndexChanged += (_, _) => api.Enabled = mode.SelectedIndex == 1; stack.Controls.Add(api);
        Paragraph(stack, "两种方案效果不同。已配置游戏请先恢复，再切换方案。");
        var save = Action("保存", "Save", async (_, _) => { if (!await RequireFeatureAsync("configuration.edit") || dialog.IsDisposed) return; installMode.SelectedIndex = mode.SelectedIndex; optiVulkan = api.SelectedIndex == 1; dialog.DialogResult = DialogResult.OK; });
        stack.Controls.Add(save);
        var diagnostics = Action("查看诊断", "Diagnostics", (_, _) => { dialog.Close(); ShowDiagnosticsMenu(); });
        diagnostics.Width = 160; stack.Controls.Add(diagnostics);
        dialog.Controls.Add(stack); dialog.ShowDialog(this);
    }
    Form ProductDialog(string title, int width = 560, int height = 330)
    {
        var form = new Form
        {
            Text = title, ClientSize = new Size(width, height), Font = Font, BackColor = Surface, ForeColor = ink,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            MinimizeBox = false, Padding = new Padding(26), AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi
        };
        ThemeWindow(form); return form;
    }
    void ShowConfigurationComplete(string exe, bool opti)
    {
        SwitchPage(1);
        status.Text = opti
            ? "开启成功 · OptiScaler 组件已部署。进入游戏后按 Insert 验证实际效果。"
            : "开启成功 · 组件已部署。进入游戏后开启 FSR、按 End 验证实际效果。";
        ShowProductToast("开启成功 · 可以启动游戏", true);
    }

    void ShowInstallationFailure(Exception error, string exe)
    {
        if (IsDisposed || Disposing) return;
        var missingRuntime = error is MissingAmdHipRuntimeException;
        var title = missingRuntime ? "开启失败：缺少 AMD 运行时" : "开启 DLSS5 失败";
        var advice = missingRuntime
            ? "如果你使用 NVIDIA 显卡：请勿继续此 AMD 安装方案，也不要在上游提示中强行输入 y。此提示不是 NVIDIA 驱动损坏。\r\n\r\n如果你使用 AMD 显卡：请检查上游支持的显卡型号和 AMD Adrenalin 驱动。\r\n\r\n若游戏显示配置未完成，请先使用“恢复配置”清理本次安装，再重试。"
            : "安装未成功。请保留下面的错误详情；若游戏显示配置未完成，请先恢复配置再重试。";
        var fullText = title + "\r\n\r\n" + error.Message + "\r\n\r\n处理建议\r\n" + advice +
            "\r\n\r\n技术详情（路径等敏感信息已脱敏）\r\n" + Diagnostics.Redact(error.ToString(), exe);
        using var dialog = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(700, 480), MinimumSize = new Size(460, 340),
            BackColor = Surface, ForeColor = Ink, Font = Font,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        var heading = new Label
        {
            Dock = DockStyle.Top, Height = 64, Padding = new Padding(20, 12, 20, 8),
            Text = "⚠ " + title, ForeColor = Color.FromArgb(255, 120, 100),
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold)
        };
        var details = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Text = fullText, BorderStyle = BorderStyle.None,
            BackColor = Surface, ForeColor = Ink, Font = new Font(Font.FontFamily, 11)
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 12), BackColor = Surface };
        body.Controls.Add(details);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(20, 8, 20, 8),
            FlowDirection = FlowDirection.RightToLeft, BackColor = Surface
        };
        var close = new Button { Text = "知道了", Width = 100, Height = 38, DialogResult = DialogResult.OK };
        var copy = new Button { Text = "复制完整错误", Width = 140, Height = 38 };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(fullText); copy.Text = "已复制"; }
            catch { MessageBox.Show(dialog, "无法访问剪贴板，可在详情中选择文本复制。", "复制失败"); }
        };
        actions.Controls.Add(close); actions.Controls.Add(copy);
        dialog.Controls.Add(body); dialog.Controls.Add(actions); dialog.Controls.Add(heading);
        dialog.AcceptButton = close; dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }

    void ShowProductToast(string message, bool success)
    {
        productToastTimer?.Stop(); productToastTimer?.Dispose(); productToastTimer = null;
        if (productToast != null) { pageHost.Controls.Remove(productToast); productToast.Dispose(); }
        var toast = productToast = new RoundedPanel
        {
            Size = new Size(370, 58), Radius = 17, BackColor = Surface,
            BorderColor = success ? Acid : Line, Padding = new Padding(16, 8, 16, 8)
        };
        toast.Controls.Add(new Label
        {
            Text = message, Dock = DockStyle.Fill, ForeColor = success ? Acid : Ink,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter
        });
        pageHost.Controls.Add(toast); toast.BringToFront();
        toast.Location = new Point(Math.Max(12, pageHost.ClientSize.Width - toast.Width - 26), 20);
        productToastTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        productToastTimer.Tick += (_, _) =>
        {
            productToastTimer?.Stop(); productToastTimer?.Dispose(); productToastTimer = null;
            if (productToast != null) { pageHost.Controls.Remove(productToast); productToast.Dispose(); productToast = null; }
        };
        productToastTimer.Start();
    }

    async Task EnableSelectedDlssAsync()
    {
        if (selectedGame == null || busy || configureAnimating) return;
        configureAnimating = true;
        var cover = selectedCard?.Controls.OfType<PictureBox>().FirstOrDefault()?
            .Controls.OfType<DlssCoverAction>().FirstOrDefault();
        if (cover is { IsDisposed: false }) { cover.Progress = 4; cover.Loading = true; cover.Visible = true; }
        install.Text = english ? "Enabling…" : "正在开启…";
        try { await InstallAsync(); }
        finally
        {
            configureAnimating = false;
            install.Text = english ? "Enable DLSS5" : "开启 DLSS5";
            if (cover is { IsDisposed: false }) { cover.Loading = false; cover.Visible = false; }
        }
    }
    async void LaunchSelectedGame() { if (selectedGame != null && !busy) await LaunchGameAsync(selectedGame); }
    async Task LaunchGameAsync(string exe)
    {
        if (busy || !await RequireFeatureAsync("game.launch") || busy) return;
        try
        {
            Core.ValidateGame(exe);
            var game = libraryGames.FirstOrDefault(g => string.Equals(g.ExePath, exe, StringComparison.OrdinalIgnoreCase));
            var id = game?.SteamAppId;
            var info = id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9]+$")
                ? new ProcessStartInfo("steam://rungameid/" + id) { UseShellExecute = true }
                : new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            Process.Start(info); status.Text = "已发送启动请求：" + (game?.Title ?? Path.GetFileNameWithoutExtension(exe));
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "启动失败"); }
    }
    async void ShowDiagnosticsMenu()
    {
        if (!await RequireFeatureAsync("diagnostics.use")) return;
        if (selectedGame == null) { SwitchPage(1); status.Text = "请先选择游戏。"; return; }
        if (busy) return;
        using var dialog = ProductDialog("诊断与运行状态", 540, 290);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        stack.Controls.Add(Heading("诊断与运行状态", "Diagnostics", 19));
        foreach (var pair in new[] { ("兼容性 / 安装记录", 0), ("刷新运行状态", 1), ("导出诊断", 2), ("F8 控制面板", 3) })
        {
            var b = Action(pair.Item1, pair.Item1, async (_, _) => { dialog.Close(); if (pair.Item2 == 0) await InspectSelectedAsync(false); else if (pair.Item2 == 1) await InspectSelectedAsync(true); else if (pair.Item2 == 2) await ExportDiagnosticAsync(); else OpenPanel(); });
            b.Width = 240; stack.Controls.Add(b);
        }
        dialog.Controls.Add(stack); dialog.ShowDialog(this);
    }
    bool IsConfigured(string exe)
    {
        // A library badge is not an integrity check. Do not hash a large runtime DLL
        // on every keystroke or layout; installation/diagnostics perform full validation.
        try
        {
            if (GameManagement.Read(exe) is { } record) return record.Phase == "installed";
            var dir = Path.GetDirectoryName(exe)!;
            return File.Exists(Path.Combine(dir, Core.DllName))
                && File.Exists(Path.Combine(dir, "dlssnr_on_amd.ini"))
                && File.Exists(Path.Combine(dir, "dlssnr_on_amd_weights.bin"))
                && Core.ProxyNames.Any(n => File.Exists(Path.Combine(dir, n)));
        }
        catch { return false; }
    }
    void FilterGames()
    {
        var states = libraryGames.ToDictionary(g => g.ExePath, GetLibraryState, StringComparer.OrdinalIgnoreCase);
        var configuredCount = states.Values.Count(s => s == LibraryFilter.Configured);
        if (libraryAllTab != null) libraryAllTab.Text = english ? $"All {libraryGames.Count}" : $"全部 {libraryGames.Count}";
        if (libraryConfiguredTab != null) libraryConfiguredTab.Text = english ? $"Configured {configuredCount}" : $"已配置 {configuredCount}";
        if (unconfiguredTab != null) unconfiguredTab.Text = (english ? "Unconfigured " : "未配置 ") + states.Values.Count(s => s == LibraryFilter.Unconfigured);
        if (attentionTab != null) attentionTab.Text = (english ? "Unsupported " : "不支持 ") + states.Values.Count(s => s == LibraryFilter.Unsupported);
        libraryCount.Text = english ? $"{libraryGames.Count} games found" : $"已发现 {libraryGames.Count} 个游戏";
        var matches = libraryGames.Where(g => g.Title.Contains(librarySearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            && (libraryFilter == LibraryFilter.All || states[g.ExePath] == libraryFilter)).ToList();
        var visible = matches.Select(g => g.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Control c in games.Controls)
            c.Visible = c.Tag is GameCandidate g && visible.Contains(g.ExePath);
        emptyLibrary.Visible = matches.Count == 0;
        emptyLibrary.Text = libraryGames.Count == 0 ? "尚未发现游戏 · 添加游戏或重新扫描" : "没有匹配的游戏 · 尝试其他筛选或搜索词";
        LayoutV2Sections();
    }
    void LayoutGameCards()
    {
        int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
        int available = games.ClientSize.Width - games.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
        const int columns = 5;
        int width = Math.Max(S(100), (available - columns * S(16)) / columns);
        games.SuspendLayout();
        foreach (Control card in games.Controls)
        {
            card.Margin = new Padding(0, S(8), S(16), S(16));
            card.Width = width;
            if (card.Controls.OfType<PictureBox>().FirstOrDefault() is not { } pic) continue;
            pic.SetBounds(S(6), S(6), width - S(12), (int)Math.Round((width - S(12)) * 394d / 309d));
            if (pic is CoverPictureBox cover && card is RoundedPanel panel)
                cover.CornerRadius = Math.Max(1, panel.Radius - 6);
            var labels = card.Controls.OfType<Label>().ToArray();
            if (labels.Length >= 2) { labels[0].SetBounds(S(12), pic.Bottom + S(10), width - S(24), S(25)); labels[1].SetBounds(S(12), pic.Bottom + S(37), width - S(24), S(24)); }
            card.Height = pic.Bottom + S(72);
        }
        games.ResumeLayout();
    }
    DownloadSession BeginDownloadSession(string title, Func<Task> retry, string featureKey)
    {
        DownloadRow? row = null;
        var session = new DownloadSession();
        activeAccountDownload = session;
        session.Disposed += () => { if (ReferenceEquals(activeAccountDownload, session)) activeAccountDownload = null; };
        session.Changed = snapshot =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            void Apply()
            {
                if (IsDisposed) return;
                row ??= AddDownloadRow(session, title, retry, featureKey);
                row.State = snapshot.State; row.Percent = snapshot.Percent; row.Size = snapshot.Size; row.Detail = snapshot.Detail;
                row.Info.Text = $"{title}\n{snapshot.State} · {snapshot.Percent}% · {snapshot.Size / 1048576d:0.00} MB\n{snapshot.Detail}";
                row.Bar.Value = Math.Clamp(snapshot.Percent, 0, 100);
                var activeCover = selectedCard?.Controls.OfType<PictureBox>().FirstOrDefault()?
                    .Controls.OfType<DlssCoverAction>().FirstOrDefault();
                if (activeCover is { IsDisposed: false, Loading: true })
                    activeCover.Progress = 4 + (int)Math.Round(snapshot.Percent * .82);
                row.Pause.Enabled = snapshot.State is "下载中" or "已暂停"; row.Pause.Text = session.Paused ? "继续" : "暂停";
                row.Cancel.Enabled = snapshot.State is "下载中" or "已暂停" or "连接中";
                row.Retry.Visible = snapshot.State is "下载失败" or "已取消";
                RenderDownloads();
            }
            if (InvokeRequired) BeginInvoke((Action)Apply); else Apply();
        };
        DownloadSources.CurrentSession.Value = session; return session;
    }
    DownloadRow AddDownloadRow(DownloadSession session, string title, Func<Task> retry, string featureKey)
    {
        var card = new RoundedPanel { Width = Math.Max(280, downloadList.ClientSize.Width - 28), Height = 190, BackColor = Surface, Radius = 5, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 14) };
        var info = new Label { Dock = DockStyle.Top, Height = 78, ForeColor = ink, AutoEllipsis = true };
        var bar = new AccentProgressBar { Dock = DockStyle.Top, Height = 14 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        var pause = Action("暂停", "Pause", async (_, _) => { if (!session.Paused || await RequireFeatureAsync(featureKey)) session.TogglePause(); }); pause.Width = 76;
        var cancel = Action("取消", "Cancel", (_, _) => session.Cancel()); cancel.Width = 76;
        var again = Action("重试", "Retry", async (_, _) => { if (busy) { MessageBox.Show(this, "请等待当前操作结束。", Text); return; } await retry(); }); again.Width = 76;
        var row = new DownloadRow { Session = session, Card = card, Info = info, Bar = bar, Pause = pause, Cancel = cancel, Retry = again, Title = title };
        var detail = Action("详情", "Details", (_, _) => MessageBox.Show(this, row.Title + "\n" + row.State + "\n" + row.Detail, "下载任务")); detail.Width = 76;
        actions.Controls.AddRange(new Control[] { pause, cancel, again, detail });
        card.Controls.Add(bar); card.Controls.Add(info); card.Controls.Add(actions); downloadRows.Insert(0, row); downloadList.Controls.Add(card); downloadList.Controls.SetChildIndex(card, 0); return row;
    }
    void RenderDownloads()
    {
        foreach (var row in downloadRows) row.Card.Visible = showCompleted == (row.State == "已完成 · 校验通过");
        var empty = downloadList.Controls.Find("empty", false).FirstOrDefault();
        if (empty == null) { empty = new Label { Name = "empty", AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = muted, Padding = new Padding(12, 36, 12, 36) }; downloadList.Controls.Add(empty); }
        empty.Text = showCompleted ? "暂无完成记录。" : "没有正在下载的任务。\n\n点击“开启 DLSS5”后，所需组件会出现在这里。";
        empty.Visible = !downloadRows.Any(r => showCompleted == (r.State == "已完成 · 校验通过"));
    }
}
