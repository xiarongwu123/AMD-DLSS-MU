using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    Color ink => Ink;
    Color muted => Muted;
    readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    readonly Panel libraryPage = new HudPanel { Dock = DockStyle.Fill };
    readonly List<(Control control, string zh, string en)> translations = new();
    readonly List<Button> navigation = new();
    readonly List<int> navigationPages = new();
    List<GameCandidate> libraryGames = new();
    readonly TextBox librarySearch = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, PlaceholderText = "搜索游戏 / Search games" };
    readonly Label shellStatus = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button launch = new RoundedButton { Text = "启动游戏", Visible = false };
    readonly RoundedButton themeToggle = new();
    readonly AccentProgressBar libraryDownloadProgress = new() { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 12, 0, 12) };
    bool configureAnimating;
    readonly Label libraryCount = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    UnderlineTabButton? libraryAllTab;
    UnderlineTabButton? libraryConfiguredTab;
    System.Windows.Forms.Timer? pageTransition;
    Control? transitioningPage;
    int activePage;
    T Localize<T>(T c, string zh, string en) where T : Control { translations.Add((c, zh, en)); c.Text = english ? en : zh; return c; }
    Label Heading(string zh, string en, float size = 14) => Localize(new Label { AutoSize = true, ForeColor = ink, Font = new Font(Font.FontFamily, size), Margin = new Padding(0, 8, 0, 8), Padding = Padding.Empty }, zh, en);
    Label PageTitle(string zh, string en) => Localize(new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = ink, Font = new Font(Font.FontFamily, 21, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, Padding = Padding.Empty }, zh, en);
    RoundedButton Action(string zh, string en, EventHandler click)
    {
        var b = Localize(new RoundedButton { Size = new Size(134, 40), BackColor = Surface, ForeColor = ink, Cursor = Cursors.Hand, Radius = 10, Margin = new Padding(0, 0, 10, 0) }, zh, en);
        b.Click += click; return b;
    }
    public MainForm()
    {
        LoadPreferences();
        Text = "AMD DLSS MU · 2.0 Preview"; Size = new Size(1440, 910); MinimumSize = new Size(1200, 740);
        Font = new Font("Microsoft YaHei UI", 10f); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Line; ForeColor = ink; Padding = new Padding(1);
        FormBorderStyle = FormBorderStyle.None;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Base };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.Controls.Add(BuildTitleBar(), 0, 0);
        root.Controls.Add(BuildV2Navigation(), 0, 1);
        pageHost.Margin = Padding.Empty; pageHost.BackColor = Base; root.Controls.Add(pageHost, 0, 2);
        shellStatus.ForeColor = muted; shellStatus.Padding = new Padding(16, 0, 0, 0);
        var foot = new Panel { Dock = DockStyle.Fill, BackColor = Sidebar, Margin = Padding.Empty };
        var version = new Label { Text = "AMD DLSS MU  ·  v" + AutoUpdate.DisplayVersion, Dock = DockStyle.Right, Width = 230, ForeColor = muted, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 16, 0) };
        foot.Controls.Add(shellStatus); foot.Controls.Add(version); root.Controls.Add(foot, 0, 3);
        Controls.Add(root);
        BuildLibrary(); BuildV2Home(); BuildAbout(); BuildProductPages(); BuildAccountPage(); SwitchPage(0); ApplyTheme(darkMode, false);
        status.TextChanged += (_, _) => shellStatus.Text = status.Text;
        shellStatus.Text = status.Text;
        Shown += async (_, _) => { await ScanGamesAsync(); if (autoCheckUpdates) await CheckAppUpdateAsync(true); };
        FormClosing += (_, e) => { if (busy && !lifetime.IsCancellationRequested) { e.Cancel = true; MessageBox.Show(this, "请等待当前操作完成，或先在下载任务中取消下载。", Text); } };
        FormClosed += (_, _) => { pageTransition?.Stop(); pageTransition?.Dispose(); controlPanel?.Close(); lifetime.Cancel(); configurationSurface.Dispose(); };
    }

    Control BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Base, Margin = Padding.Empty };
        var brand = new Label
        {
            Text = "  AMD DLSS MU", Dock = DockStyle.Left, Width = 190, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = muted, Font = new Font(Font.FontFamily, 9.5f), Padding = new Padding(18, 0, 0, 0)
        };
        brand.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(Acid);
            e.Graphics.FillEllipse(brush, 14, (brand.Height - 8) / 2, 8, 8);
        };
        var captions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 144, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        Button Caption(string text, EventHandler click)
        {
            var button = new Button
            {
                Text = text, Size = new Size(48, 40), FlatStyle = FlatStyle.Flat, BackColor = Base,
                ForeColor = muted, Margin = Padding.Empty, Cursor = Cursors.Hand, TabStop = false
            };
            button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Surface;
            button.Click += click; return button;
        }
        captions.Controls.Add(Caption("—", (_, _) => WindowState = FormWindowState.Minimized));
        captions.Controls.Add(Caption("□", (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
        var close = Caption("×", (_, _) => Close()); close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 52); close.ForeColor = ink; captions.Controls.Add(close);
        var quick = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 320, Height = 42, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        var appearance = new RoundedButton { Size = new Size(42, 40), Text = "", Glyph = darkMode ? "\uE706" : "\uE708", Radius = 20, BackColor = Surface, ForeColor = muted, Margin = new Padding(0, 0, 8, 0) };
        appearance.Click += (_, _) => { ToggleTheme(); appearance.Glyph = darkMode ? "\uE706" : "\uE708"; appearance.Invalidate(); };
        var language = new RoundedButton { Size = new Size(70, 40), Text = english ? "EN" : "ZH", Radius = 18, BackColor = Surface, ForeColor = ink, Margin = Padding.Empty };
        language.Click += (_, _) => { english = !english; ApplyLanguage(english); language.Text = english ? "EN" : "ZH"; };
        var account = Action("登录 / 注册", "Account", (_, _) => SwitchPage(6)); account.Size = new Size(104, 38); account.Radius = 2;
        var settings = Action("设置", "Settings", (_, _) => SwitchPage(5)); settings.Size = new Size(62, 38); settings.Radius = 2;
        quick.Controls.Add(appearance); quick.Controls.Add(language); quick.Controls.Add(account); quick.Controls.Add(settings);
        void BeginDrag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragWindow(); }
        bar.MouseDown += BeginDrag; brand.MouseDown += BeginDrag;
        bar.DoubleClick += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        brand.DoubleClick += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        bar.Controls.Add(brand); bar.Controls.Add(quick); bar.Controls.Add(captions); return bar;
    }

    void BuildLibrary()
    {
        var rows = libraryRows = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 142)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(28, 18, 28, 0), Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        toolbar.Controls.Add(PageTitle("游戏库", "Game library"), 0, 0);
        libraryCount.ForeColor = muted;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        var add = Action("添加游戏", "Add game", SelectGameManually); add.Size = new Size(112, 46); add.Radius = 12; actions.Controls.Add(add);
        scan.AutoSize = false; scan.Size = new Size(112, 46); scan.Margin = Padding.Empty; scan.BackColor = SelectedSurface; scan.ForeColor = Acid; scan.AccessibleName = "重新扫描游戏";
        if (scan is RoundedButton scanButton) { scanButton.Glyph = ""; scanButton.Radius = 12; }
        scan.Text = english ? "Rescan" : "重新扫描"; scan.Click += async (_, _) => await ScanGamesAsync(); actions.Controls.Add(scan); toolbar.Controls.Add(actions, 1, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0), Margin = Padding.Empty };
        var all = libraryAllTab = new UnderlineTabButton { Selected = true, Size = new Size(112, 45), Font = new Font(Font.FontFamily, 10f, FontStyle.Bold) };
        var configured = libraryConfiguredTab = new UnderlineTabButton { Size = new Size(112, 45), Font = new Font(Font.FontFamily, 10f, FontStyle.Bold) };
        all.Click += (_, _) => SetLibraryFilter(LibraryFilter.All);
        configured.Click += (_, _) => SetLibraryFilter(LibraryFilter.Configured);
        unconfiguredTab = new UnderlineTabButton { Size = new Size(120, 45) };
        attentionTab = new UnderlineTabButton { Size = new Size(120, 45) };
        unconfiguredTab.Click += (_, _) => SetLibraryFilter(LibraryFilter.Unconfigured);
        attentionTab.Click += (_, _) => SetLibraryFilter(LibraryFilter.Attention);
        filters.Controls.Add(all); filters.Controls.Add(configured); filters.Controls.Add(unconfiguredTab); filters.Controls.Add(attentionTab);
        toolbar.Controls.Add(filters, 0, 1); toolbar.SetColumnSpan(filters, 2);
        games.Padding = new Padding(28, 12, 14, 16); games.BackColor = Base; games.Margin = Padding.Empty;
        games.AllowDrop = true;
        games.DragEnter += (_, e) => e.Effect = !busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        games.DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) AddPath(paths[0]); };
        games.ClientSizeChanged += (_, _) => LayoutGameCards();
        var footerSurface = configurationSurface = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Surface, Radius = 2, Padding = new Padding(1), Margin = Padding.Empty };
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, ColumnCount = 2, RowCount = 3, Padding = new Padding(20, 13, 20, 10), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        selected.AutoSize = status.AutoSize = false; selected.AutoEllipsis = status.AutoEllipsis = true; selected.Dock = status.Dock = DockStyle.Fill;
        selected.Font = new Font(Font.FontFamily, 13); selected.ForeColor = ink; status.ForeColor = muted;
        footer.Controls.Add(selected, 0, 0); footer.Controls.Add(status, 0, 1);
        installMode.Items.AddRange(new[] { "模式一 · 神经渲染（推荐）", "模式二 · OptiScaler" }); installMode.SelectedIndex = 0;
        installMode.BackColor = Surface; installMode.ForeColor = ink;
        var mainAction = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0), Margin = Padding.Empty };
        foreach (var b in new[] { install, launch }) { b.AutoSize = false; b.Dock = DockStyle.Fill; b.BackColor = Acid; b.ForeColor = OnAccent; b.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold); if (b is RoundedButton rb) { rb.Chamfer = true; rb.Radius = 16; rb.Glyph = b == install ? "" : "\uE768"; rb.TrailingArrow = b == install; rb.LightningEffect = b == install; } mainAction.Controls.Add(b); }
        install.Click += async (_, _) =>
        {
            if (busy || configureAnimating) return;
            configurationDialog?.Close();
            SwitchPage(3);
            configureAnimating = true;
            try { if (!IsDisposed) await InstallAsync(); }
            finally { configureAnimating = false; }
        };
        launch.Click += (_, _) => { configurationDialog?.Close(); LaunchSelectedGame(); };
        footer.Controls.Add(mainAction, 1, 0); footer.SetRowSpan(mainAction, 2);
        var secondary = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var advanced = Action("高级选项", "Advanced", (_, _) => ShowAdvanced()); advanced.Width = 92; secondary.Controls.Add(advanced);
        restore.AutoSize = false; restore.Size = new Size(94, 36); restore.BackColor = Surface; restore.ForeColor = muted; restore.Click += async (_, _) => { configurationDialog?.Close(); await RestoreAsync(); }; secondary.Controls.Add(restore);
        var diag = Action("查看诊断", "Diagnostics", (_, _) => { configurationDialog?.Close(); ShowDiagnosticsMenu(); }); diag.Width = 98; secondary.Controls.Add(diag);
        footer.Controls.Add(secondary, 0, 2);
        footer.Controls.Add(libraryDownloadProgress, 1, 2);
        footerSurface.Controls.Add(footer);
        var gridHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        emptyLibrary.Dock = DockStyle.Fill; gridHost.Controls.Add(games); gridHost.Controls.Add(emptyLibrary); emptyLibrary.BringToFront();
        rows.Controls.Add(toolbar, 0, 0); rows.Controls.Add(gridHost, 0, 1); rows.Controls.Add(BuildLibraryPager(), 0, 2);
        libraryPage.Controls.Add(rows); pageHost.Controls.Add(libraryPage);
    }
    void SwitchPage(int index)
    {
        var changed = activePage != index;
        activePage = index; libraryPage.Visible = index is 0 or 1; if (aboutPanel != null) aboutPanel.Visible = index == 2;
        downloadsPage.Visible = index == 3; helpPage.Visible = index == 4; settingsPage.Visible = index == 5;
        accountPage.Visible = index == 6;
        if (index is 0 or 1) ScrollToLibrarySection(index == 1);
        for (int i = 0; i < navigation.Count; i++) { bool current = navigationPages[i] == index; navigation[i].BackColor = current ? SelectedSurface : Sidebar; navigation[i].ForeColor = current ? Acid : muted; ((RoundedButton)navigation[i]).Active = current; navigation[i].Invalidate(); }
        if (index == 3) RenderDownloads();
        ApplyTheme(darkMode, false);
        if (changed)
        {
            var page = index switch { 0 or 1 => libraryPage, 2 => aboutPanel, 3 => downloadsPage, 4 => helpPage, 5 => settingsPage, 6 => accountPage, _ => null };
            if (page != null && index is not 0 and not 1) AnimatePageEntrance(page);
        }
    }

    void AnimatePageEntrance(Control page)
    {
        pageTransition?.Stop(); pageTransition?.Dispose();
        pageTransition = null;
        if (transitioningPage is { IsDisposed: false }) transitioningPage.Dock = DockStyle.Fill;
        transitioningPage = null;
        var finalBounds = pageHost.ClientRectangle;
        if (finalBounds.Width <= 0 || finalBounds.Height <= 0) return;
        if (!SystemInformation.IsMenuAnimationEnabled) { page.Dock = DockStyle.Fill; return; }
        transitioningPage = page;
        page.BringToFront(); page.Dock = DockStyle.None;
        var startOffset = Math.Max(10, (int)Math.Round(18 * DeviceDpi / 96d));
        page.Bounds = new Rectangle(finalBounds.X + startOffset, finalBounds.Y, finalBounds.Width, finalBounds.Height);
        var frame = 0;
        pageTransition = new System.Windows.Forms.Timer { Interval = 15 };
        pageTransition.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1d, frame / 9d);
            var eased = 1d - Math.Pow(1d - t, 3d);
            page.Left = finalBounds.X + (int)Math.Round(startOffset * (1d - eased));
            if (t < 1d) return;
            pageTransition!.Stop(); pageTransition.Dispose(); pageTransition = null;
            page.Bounds = finalBounds; page.Dock = DockStyle.Fill;
            transitioningPage = null;
        };
        pageTransition.Start();
    }
    void RefreshHome()
    {
        foreach (Control card in games.Controls)
        {
            if (card.Tag is not GameCandidate game) continue;
            var labels = card.Controls.OfType<Label>().ToArray();
            if (labels.Length > 1) labels[1].Text = IsConfigured(game.ExePath)
                ? (english ? "Configured · runtime unverified" : "已配置 · 待验证")
                : (english ? "Not configured" : "未配置");
        }
        FilterGames();
        RenderHomeCards();
    }
    void BrowseFolder() { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) AddPath(d.SelectedPath); }
    void AddPath(string path)
    {
        if (busy) return;
        if (Directory.Exists(path)) { var exe = GameScanner.FindGameExe(path); if (exe == null) { SelectGameManually(this, EventArgs.Empty); return; } path = exe; }
        try
        {
            path = Path.GetFullPath(path);
            Core.ValidateGame(path);
            GameLibrary.AddManual(path);
            librarySearch.Clear();
            var game = libraryGames.FirstOrDefault(g => string.Equals(g.ExePath, path, StringComparison.OrdinalIgnoreCase));
            if (game == null)
            {
                game = new GameCandidate { Title = Path.GetFileNameWithoutExtension(path), ExePath = path, InstallDirectory = Path.GetDirectoryName(path)! };
                libraryGames.Add(game);
                games.Controls.Add(CreateGameCard(game));
            }
            if (selectedCard != null)
            {
                selectedCard.Selected = false;
                selectedCard.Hovered = false;
                foreach (var badge in selectedCard.Controls.OfType<GameSelectionBadge>()) badge.Visible = false;
                selectedCard.Invalidate();
            }
            selectedGame = path;
            selected.Text = (english ? "Selected: " : "已选择：") + game.Title;
            SwitchPage(1);
            var card = games.Controls.Cast<Control>().FirstOrDefault(c => c.Tag is GameCandidate g && string.Equals(g.ExePath, path, StringComparison.OrdinalIgnoreCase));
            if (card != null)
            {
                games.ScrollControlIntoView(card);
                if (card is RoundedPanel rounded)
                {
                    selectedCard = rounded;
                    rounded.Selected = true;
                    foreach (var badge in rounded.Controls.OfType<GameSelectionBadge>()) { badge.Visible = true; badge.BringToFront(); }
                    rounded.Invalidate();
                }
            }
            RevealGame(game);
            RenderHomeCards();
            status.Text = english ? "Added and saved. Ready to configure." : "已添加到游戏库，悬停封面开启 DLSS5。";
            LayoutGameCards();
            UpdateButtons();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, english ? "Could not add game" : "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    void ApplyLanguage(bool en)
    {
        foreach (var (control, zh, englishText) in translations) control.Text = en ? englishText : zh;
        install.Text = en ? "Enable DLSS5" : "开启 DLSS5"; restore.Text = en ? "Restore" : "恢复配置";
        scan.Text = en ? "Rescan" : "重新扫描";
        launch.Text = en ? "Launch game" : "启动游戏";
        UpdateThemeToggleText();
        var selectedIndex = installMode.SelectedIndex;
        installMode.Items.Clear();
        installMode.Items.Add(en ? "Mode 1 (recommended) — official runtime" : "模式一（推荐）— 官方运行时");
        installMode.Items.Add(en ? "Mode 2 — OptiScaler (upscaling / FG)" : "模式二 — OptiScaler 标准版（超分/帧生成）");
        installMode.SelectedIndex = Math.Max(0, selectedIndex);
        RefreshHome();
    }
}
