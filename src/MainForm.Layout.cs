using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    readonly Panel libraryPage = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel recentCards = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 16) };
    readonly List<(Control control, string zh, string en)> translations = new();
    readonly List<Button> navigation = new();
    List<GameCandidate> libraryGames = new();
    int activePage;
    readonly Color ink = Color.FromArgb(27, 40, 53);
    readonly Color muted = Color.FromArgb(112, 130, 146);
    T Localize<T>(T c, string zh, string en) where T : Control { translations.Add((c, zh, en)); c.Text = english ? en : zh; return c; }
    Label Heading(string zh, string en, float size = 14) => Localize(new Label { AutoSize = true, ForeColor = ink, Font = new Font(Font.FontFamily, size, FontStyle.Bold), Margin = new Padding(0, 12, 0, 12) }, zh, en);
    RoundedButton Action(string zh, string en, EventHandler click)
    {
        var b = Localize(new RoundedButton { Size = new Size(144, 44), AutoSize = false, BackColor = Color.White, ForeColor = ink, Cursor = Cursors.Hand, Margin = new Padding(8, 0, 0, 0) }, zh, en);
        b.Click += click; return b;
    }
    public MainForm()
    {
        Text = "AMD DLSS MU"; Size = new Size(1380, 940); MinimumSize = new Size(1000, 720);
        Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(235, 241, 245);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var side = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.White, Radius = 26, Padding = new Padding(16), Margin = new Padding(0, 0, 20, 0) };
        var sideRows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = Color.White };
        sideRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        sideRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        sideRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sideRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(20, 12, 20, 22) };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.amd_dlss_mu_logo.png"))
            if (stream != null) { using var img = Image.FromStream(stream); logo.Image = new Bitmap(img); }
        sideRows.Controls.Add(logo, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var labels = new[] { ("首页", "Home"), ("游戏", "Games"), ("关于", "About") };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i; var b = Action(labels[i].Item1, labels[i].Item2, (_, _) => SwitchPage(index));
            b.Glyph = new[] { "\uE80F", "\uE7FC", "\uE946" }[i]; b.Size = new Size(190, 50); b.Margin = new Padding(0, 0, 0, 8); navigation.Add(b); nav.Controls.Add(b);
            b.MouseEnter += (_, _) => { if (activePage != index) b.BackColor = Color.FromArgb(246, 248, 250); };
            b.MouseLeave += (_, _) => { if (activePage != index) b.BackColor = Color.White; };
        }
        sideRows.Controls.Add(nav, 0, 1);
        var ready = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(243, 246, 248), Padding = new Padding(16), Radius = 18 };
        ready.Controls.Add(Localize(new Label { Dock = DockStyle.Fill, ForeColor = muted, AutoSize = false }, "● 就绪\n\nAMD DLSS MU\nv" + AutoUpdate.DisplayVersion, "● Ready\n\nAMD DLSS MU\nv" + AutoUpdate.DisplayVersion));
        sideRows.Controls.Add(ready, 0, 3); side.Controls.Add(sideRows); root.Controls.Add(side, 0, 0);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0) };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        top.Controls.Add(Action("中文 / EN", "EN / 中文", (_, _) => { english = !english; ApplyLanguage(english); }));
        top.Controls.Add(Action("检查更新", "Check updates", async (_, _) => await CheckAppUpdateAsync(false)));
        main.Controls.Add(top, 0, 0); main.Controls.Add(pageHost, 0, 1); root.Controls.Add(main, 1, 0); Controls.Add(root);
        BuildLibrary(); BuildHome();
        aboutPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32), BackColor = Color.White };
        aboutPanel.Controls.Add(Localize(new Label { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 14), ForeColor = ink }, "关于 AMD DLSS MU\n\n由 codeXia 开发\n\n价格：免费\n\n有问题请联系微信 13657964696", "About AMD DLSS MU\n\nDeveloped by codeXia\n\nPrice: Free\n\nWeChat: 13657964696"));
        pageHost.Controls.Add(aboutPanel); SwitchPage(0);
        Shown += async (_, _) => { await ScanGamesAsync(); await CheckAppUpdateAsync(true); }; FormClosed += (_, _) => lifetime.Cancel();
    }
    void BuildLibrary()
    {
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 118)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 316));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        toolbar.Controls.Add(Heading("游戏库", "Game library", 18), 0, 0);
        var shell = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 12, 14, 10), Margin = new Padding(0, 0, 12, 4) };
        var search = new TextBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Top, PlaceholderText = "搜索游戏 / Search games" };
        search.TextChanged += (_, _) => { foreach (Control c in games.Controls) c.Visible = c.Tag is GameCandidate g && g.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase); };
        shell.Controls.Add(search); toolbar.Controls.Add(shell, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(Action("＋ 添加游戏", "＋ Add game", SelectGameManually));
        scan.AutoSize = false; scan.Size = new Size(144, 44); scan.BackColor = Color.White; scan.ForeColor = ink;
        Localize(scan, "重新扫描", "Rescan"); scan.Click += async (_, _) => await ScanGamesAsync(); actions.Controls.Add(scan); toolbar.Controls.Add(actions, 1, 1);
        games.Padding = new Padding(0); games.BackColor = BackColor;
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 2, Padding = new Padding(16) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 314));
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        selected.AutoSize = status.AutoSize = false; selected.AutoEllipsis = status.AutoEllipsis = true; selected.Dock = status.Dock = DockStyle.Fill;
        details.Controls.Add(selected, 0, 0); details.Controls.Add(status, 0, 1);
        installMode.Items.Add("模式一（推荐）— 官方运行时");
        installMode.Items.Add("模式二（备用）— 模式一失效时使用");
        installMode.SelectedIndex = 0;
        installMode.SelectedIndexChanged += (_, _) => status.Text = installMode.SelectedIndex == 1
            ? (english ? "Fallback mode: use when Mode 1 cannot hook or has no effect." : "备用模式：当模式一无法挂接或没有效果时使用。")
            : (english ? "Recommended mode: official AMD runtime." : "推荐模式：官方 AMD 运行时。");
        details.Controls.Add(installMode, 0, 2); footer.Controls.Add(details, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        foreach (var b in new[] { restore, install }) { b.AutoSize = false; b.Size = new Size(148, 44); buttons.Controls.Add(b); }
        install.BackColor = Color.FromArgb(91, 177, 0); install.ForeColor = Color.White; restore.BackColor = Color.FromArgb(240, 244, 247);
        install.Click += async (_, _) => await InstallAsync(); restore.Click += async (_, _) => await RestoreAsync();
        buttons.Controls.Add(Action("兼容性 / 记录", "Compatibility", async (_, _) => await InspectSelectedAsync(false)));
        buttons.Controls.Add(Action("刷新运行状态", "Runtime status", async (_, _) => await InspectSelectedAsync(true)));
        buttons.Controls.Add(Action("导出诊断", "Diagnostics", async (_, _) => await ExportDiagnosticAsync()));
        footer.Controls.Add(buttons, 1, 0); rows.Controls.Add(toolbar, 0, 0); rows.Controls.Add(games, 0, 1); rows.Controls.Add(footer, 0, 2);
        libraryPage.Controls.Add(rows); pageHost.Controls.Add(libraryPage);
    }
    void BuildHome()
    {
        homePanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        var drop = new RoundedPanel { Height = 320, BackColor = Color.White, Radius = 22, Margin = new Padding(0, 0, 0, 22), Padding = new Padding(26) };
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.AutoSize)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var folder = new Label { Text = "\uE8B7", Font = new Font("Segoe MDL2 Assets", 38), ForeColor = Color.FromArgb(101, 185, 12), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        var title = Heading("将游戏文件夹拖到这里", "Drop a game folder here", 18); title.Anchor = AnchorStyles.None; title.Margin = new Padding(0, 12, 0, 10);
        center.Controls.Add(folder, 0, 0); center.Controls.Add(title, 0, 1);
        center.Controls.Add(Localize(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = muted }, "或", "or"), 0, 2);
        var browse = Action("浏览文件夹", "Browse folders", (_, _) => BrowseFolder()); browse.Anchor = AnchorStyles.None; browse.ForeColor = Color.FromArgb(90, 165, 0); browse.BackColor = Color.FromArgb(241, 246, 248); center.Controls.Add(browse, 0, 3);
        drop.Controls.Add(center); stack.Controls.Add(drop);
        void AcceptDrop(Control control) { control.AllowDrop = true; control.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None; control.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) AddPath(paths[0]); }; foreach (Control child in control.Controls) AcceptDrop(child); }
        AcceptDrop(drop);
        stack.Controls.Add(Heading("最近的游戏", "Recent games", 15));
        homeSummary = new Label { AutoSize = true, ForeColor = muted, Margin = new Padding(0, 0, 0, 8) }; stack.Controls.Add(homeSummary); stack.Controls.Add(recentCards);
        homePanel.Controls.Add(stack);
        homePanel.ClientSizeChanged += (_, _) => { int w = Math.Max(300, homePanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8); stack.MaximumSize = new Size(w, 0); stack.Width = w; drop.Width = w; recentCards.MaximumSize = new Size(w, 0); recentCards.Width = w; homeSummary.MaximumSize = new Size(w, 0); };
        pageHost.Controls.Add(homePanel);
    }
    void SwitchPage(int index)
    {
        activePage = index; libraryPage.Visible = index == 1; if (homePanel != null) homePanel.Visible = index == 0; if (aboutPanel != null) aboutPanel.Visible = index == 2;
        for (int i = 0; i < navigation.Count; i++) { navigation[i].BackColor = i == index ? Color.FromArgb(235, 243, 233) : Color.White; navigation[i].ForeColor = i == index ? ink : muted; ((RoundedButton)navigation[i]).Active = i == index; navigation[i].Invalidate(); }
        if (index == 0) RefreshHome();
    }
    void RefreshHome()
    {
        if (homeSummary == null) return;
        while (recentCards.Controls.Count > 0) { var c = recentCards.Controls[0]; recentCards.Controls.Remove(c); c.Dispose(); }
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "configured-games.txt");
        var paths = File.Exists(file) ? File.ReadAllLines(file).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Reverse().ToList() : new List<string>();
        if (Directory.Exists(GameManagement.Root))
            foreach (var recordPath in Directory.EnumerateFiles(GameManagement.Root, "record.json", SearchOption.AllDirectories))
            {
                try
                {
                    Core.RejectLinks(recordPath);
                    var record = System.Text.Json.JsonSerializer.Deserialize<GameRecord>(File.ReadAllText(recordPath));
                    if (record != null && record.Phase != "restored" && File.Exists(record.Exe) && !paths.Contains(record.Exe, StringComparer.OrdinalIgnoreCase)) paths.Insert(0, record.Exe);
                }
                catch { /* Corrupt records remain available to diagnostics; do not break the library. */ }
            }
        foreach (var path in paths)
        {
            var game = libraryGames.FirstOrDefault(g => string.Equals(g.ExePath, path, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(g.InstallDirectory) && path.StartsWith(g.InstallDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
            game ??= new GameCandidate { Title = Path.GetFileNameWithoutExtension(path), ExePath = path };
            recentCards.Controls.Add(CreateGameCard(game));
        }
        homeSummary.Text = paths.Count == 0 ? (english ? "No recent games. Add a folder above to get started." : "暂无最近游戏。拖入文件夹或浏览添加即可开始。") : (english ? $"{paths.Count} recent configuration records · click a cover to manage" : $"{paths.Count} 个最近配置记录 · 点击封面管理");
    }
    void BrowseFolder() { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) AddPath(d.SelectedPath); }
    void AddPath(string path)
    {
        if (busy) return;
        if (Directory.Exists(path)) { var exe = GameScanner.FindGameExe(path); if (exe == null) { SelectGameManually(this, EventArgs.Empty); return; } path = exe; }
        try { Core.ValidateGame(path); selectedGame = path; selected.Text = Path.GetFileNameWithoutExtension(path); SwitchPage(1); UpdateButtons(); } catch (Exception e) { MessageBox.Show(this, e.Message, Text); }
    }
    void ApplyLanguage(bool en)
    {
        foreach (var (control, zh, englishText) in translations) control.Text = en ? englishText : zh;
        install.Text = en ? "Configure" : "一键配置"; restore.Text = en ? "Restore" : "恢复配置";
        var selectedIndex = installMode.SelectedIndex;
        installMode.Items.Clear();
        installMode.Items.Add(en ? "Mode 1 (recommended) — official runtime" : "模式一（推荐）— 官方运行时");
        installMode.Items.Add(en ? "Mode 2 (fallback) — use when Mode 1 fails" : "模式二（备用）— 模式一失效时使用");
        installMode.SelectedIndex = Math.Max(0, selectedIndex);
        RefreshHome();
    }
}
