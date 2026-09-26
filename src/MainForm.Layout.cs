using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    Color ink => Ink;
    Color muted => Muted;
    readonly Panel pageHost = new BufferedPageHost { Dock = DockStyle.Fill };
    TableLayoutPanel? shellLayout;
    readonly Panel libraryPage = new LibraryScrollPanel { Dock = DockStyle.Fill };
    readonly List<(Control control, string zh, string en)> translations = new();
    readonly List<Button> navigation = new();
    readonly List<int> navigationPages = new();
    List<GameCandidate> libraryGames = new();
    readonly TextBox librarySearch = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, PlaceholderText = "搜索游戏 / Search games" };
    readonly Button launch = new RoundedButton { Text = "启动游戏", Visible = false };
    readonly RoundedButton themeToggle = new();
    bool configureAnimating;
    readonly Label libraryCount = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    UnderlineTabButton? libraryAllTab;
    UnderlineTabButton? libraryConfiguredTab;
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
        using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.AppIcon"))
            if (iconStream != null) { using var embeddedIcon = new Icon(iconStream); Icon = (Icon)embeddedIcon.Clone(); }
        ShowIcon = true;
        Text = "AMD DLSS MU · 2.0 Preview"; Size = new Size(1440, 910); MinimumSize = new Size(1200, 740);
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Line; ForeColor = ink; Padding = new Padding(1);
        FormBorderStyle = FormBorderStyle.None;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Base };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)Math.Ceiling(88 * DeviceDpi / 96d)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shellLayout = root;
        root.Controls.Add(BuildV2Navigation(), 0, 0);
        pageHost.Margin = Padding.Empty; pageHost.BackColor = Base; root.Controls.Add(pageHost, 0, 1);
        Controls.Add(root);
        BuildLibrary(); BuildV2Home(); BuildAbout(); BuildProductPages(); BuildAccountPage(); BuildMagpiePage(); SwitchPage(6); ApplyTheme(darkMode, false); ApplyAccountGate();
        Shown += async (_, _) => await RestoreAccountAsync();
        FormClosing += (_, e) => { if (busy && !lifetime.IsCancellationRequested) { e.Cancel = true; MessageBox.Show(this, "请等待当前操作完成，或先在下载任务中取消下载。", Text); } };
        FormClosed += (_, _) => { accountHeartbeat.Stop(); accountHeartbeat.Dispose(); accountClient.Changed -= AccountStateChanged; productToastTimer?.Stop(); productToastTimer?.Dispose(); controlPanel?.Close(); lifetime.Cancel(); accountClient.Dispose(); };
    }

    void BuildLibrary()
    {
        var rows = libraryRows = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 142)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
        all.Click += async (_, _) => { if (await RequireFeatureAsync("library.manage")) SetLibraryFilter(LibraryFilter.All); };
        configured.Click += async (_, _) => { if (await RequireFeatureAsync("library.manage")) SetLibraryFilter(LibraryFilter.Configured); };
        unconfiguredTab = new UnderlineTabButton { Size = new Size(120, 45) };
        attentionTab = new UnderlineTabButton { Size = new Size(120, 45) };
        unconfiguredTab.Click += async (_, _) => { if (await RequireFeatureAsync("library.manage")) SetLibraryFilter(LibraryFilter.Unconfigured); };
        attentionTab.Click += async (_, _) => { if (await RequireFeatureAsync("library.manage")) SetLibraryFilter(LibraryFilter.Unsupported); };
        filters.Controls.Add(all); filters.Controls.Add(configured); filters.Controls.Add(unconfiguredTab); filters.Controls.Add(attentionTab);
        toolbar.Controls.Add(filters, 0, 1); toolbar.SetColumnSpan(filters, 2);
        games.Padding = new Padding(28, 12, 14, 16); games.BackColor = Base; games.Margin = Padding.Empty;
        games.AllowDrop = true;
        games.DragEnter += (_, e) => e.Effect = accountClient.IsOnline && !busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        games.DragDrop += async (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) await AddPathAsync(paths[0]); };
        games.ClientSizeChanged += (_, _) => LayoutGameCards();
        installMode.Items.AddRange(new[] { "模式一 · 神经渲染（推荐）", "模式二 · OptiScaler" }); installMode.SelectedIndex = 0;
        installMode.BackColor = Surface; installMode.ForeColor = ink;
        var gridHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        emptyLibrary.Dock = DockStyle.Fill; gridHost.Controls.Add(games); gridHost.Controls.Add(emptyLibrary); emptyLibrary.BringToFront();
        // Actions live on each game card; there is no duplicate fixed footer.
        rows.Controls.Add(toolbar, 0, 0); rows.Controls.Add(gridHost, 0, 1);
        libraryPage.Controls.Add(rows); pageHost.Controls.Add(libraryPage);
    }
    void SwitchPage(int index)
    {
        if (index != 6 && !accountClient.IsOnline && !(index == 3 && busy)) index = 6;
        pageHost.SuspendLayout();
        try
        {
            activePage = index; libraryPage.Visible = index is 0 or 1; if (aboutPanel != null) aboutPanel.Visible = index == 2;
            downloadsPage.Visible = index == 3; helpPage.Visible = index == 4; settingsPage.Visible = index == 5;
            accountPage.Visible = index == 6; magpiePage.Visible = index == 7;
            SetHeaderMode(index);
            if (index is 0 or 1) ScrollToLibrarySection(index == 1);
            for (int i = 0; i < navigation.Count; i++) { bool current = navigationPages[i] == index; navigation[i].BackColor = current ? SelectedSurface : Sidebar; navigation[i].ForeColor = current ? Acid : muted; ((RoundedButton)navigation[i]).Active = current; navigation[i].Invalidate(); }
            if (index == 3) RenderDownloads();
            var page = index switch { 0 or 1 => libraryPage, 2 => aboutPanel, 3 => downloadsPage, 4 => helpPage, 5 => settingsPage, 6 => accountPage, 7 => magpiePage, _ => null };
            // Moving a page containing native text boxes and dozens of child
            // windows per frame leaves stale pixels and thrashes layout. Keep
            // page geometry stable; buttons/cards retain their own animation.
            if (page != null) { page.Dock = DockStyle.Fill; page.BringToFront(); }
        }
        finally
        {
            pageHost.ResumeLayout(true);
            // Queue one redraw after layout. Synchronous Refresh on both trees
            // exposes intermediate native child-window frames during switching.
            pageHost.Invalidate(true);
            headerNavigation?.Parent?.Invalidate(true);
        }
    }
    void RefreshHome()
    {
        libraryStateCache.Clear();
        foreach (Control card in games.Controls)
        {
            if (card.Tag is not GameCandidate game) continue;
            var labels = card.Controls.OfType<Label>().ToArray();
            if (labels.Length > 1) labels[1].Text = IsConfigured(game.ExePath)
                ? (english ? "Configured · runtime unverified" : "已配置 · 待验证")
                : (english ? "Not configured" : "未配置");
            var cover = card.Controls.OfType<PictureBox>().FirstOrDefault()?
                .Controls.OfType<DlssCoverAction>().FirstOrDefault();
            if (cover != null)
            {
                cover.English = english;
                cover.PrimaryText = IsConfigured(game.ExePath)
                    ? (english ? "Launch game" : "启动游戏") : (english ? "Enable DLSS5" : "开启 DLSS5");
            }
        }
        FilterGames();
        RenderHomeCards();
        RenderMagpieGames();
    }
    async void BrowseFolder() { if (!await RequireFeatureAsync("library.manage")) return; using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) await AddPathAsync(d.SelectedPath); }
    async Task AddPathAsync(string path)
    {
        if (busy) return;
        if (!await RequireFeatureAsync("library.manage") || busy) return;
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
            selectedGame = path;
            selected.Text = (english ? "Selected: " : "已选择：") + game.Title;
            SwitchPage(1);
            var card = games.Controls.Cast<Control>().FirstOrDefault(c => c.Tag is GameCandidate g && string.Equals(g.ExePath, path, StringComparison.OrdinalIgnoreCase));
            if (card != null)
            {
                games.ScrollControlIntoView(card);
                if (card is RoundedPanel rounded) selectedCard = rounded;
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
