using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    enum LibraryFilter { All, Configured, Unconfigured, Attention }
    LibraryFilter libraryFilter;
    int libraryPageIndex;
    TableLayoutPanel libraryRows = null!;
    RoundedPanel configurationSurface = null!;
    Form? configurationDialog;
    readonly Panel homeSection = new HudPanel { BackColor = Base };
    readonly FlowLayoutPanel homeCards = new() { WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
    readonly Label homeTotal = new() { Text = "0", ForeColor = Acid, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right, Width = 80 };
    readonly Label homeConfigured = new() { Text = "0", ForeColor = Acid, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right, Width = 80 };
    readonly Label pageNumber = new() { Width = 100, Height = 38, TextAlign = ContentAlignment.MiddleCenter };
    readonly Button previousGames = new RoundedButton { Text = "←", Width = 56, Height = 38 };
    readonly Button nextGames = new RoundedButton { Text = "→", Width = 56, Height = 38 };
    readonly Label emptyLibrary = new() { AutoSize = false, Size = new Size(650, 100), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted };
    UnderlineTabButton? unconfiguredTab, attentionTab;
    TableLayoutPanel homeOverview = null!;
    Button scrollLibrary = null!;
    bool layingOutSections;

    Control BuildV2Navigation()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Base, Padding = new Padding(24, 8, 24, 8), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand, AccessibleName = "MU 首页" };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.amd_dlss_mu_logo.png"))
            if (stream != null) { using var original = Image.FromStream(stream); logo.Image = new Bitmap(original); }
        logo.Click += (_, _) => SwitchPage(0); logo.Disposed += (_, _) => logo.Image?.Dispose();
        header.Controls.Add(logo, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var item in new[] { (0, "首页", "Home"), (1, "游戏库", "Library"), (7, "大力喜鹊", "Magpie"), (3, "下载任务", "Downloads"), (4, "帮助与反馈", "Help") })
        {
            var b = Action(item.Item2, item.Item3, (_, _) => SwitchPage(item.Item1));
            b.Size = new Size(item.Item1 == 4 ? 125 : 100, 50); b.Radius = 2; b.Margin = new Padding(0, 0, 4, 0);
            navigation.Add(b); navigationPages.Add(item.Item1); nav.Controls.Add(b);
        }
        header.Controls.Add(nav, 1, 0);
        var search = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Surface, Radius = 2, Padding = new Padding(12, 15, 12, 10), Margin = new Padding(0, 0, 12, 0) };
        librarySearch.BackColor = Surface; librarySearch.ForeColor = ink;
        librarySearch.TextChanged += (_, _) => { libraryPageIndex = 0; FilterGames(); SwitchPage(1); };
        search.Controls.Add(librarySearch); header.Controls.Add(search, 2, 0);
        var update = Action("检查更新 ↗", "Update ↗", async (_, _) => await CheckAppUpdateAsync(false));
        update.Dock = DockStyle.Fill; update.BackColor = Acid; update.ForeColor = OnAccent; update.Chamfer = true;
        header.Controls.Add(update, 3, 0);
        header.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
        return header;
    }

    Control BuildLibraryPager()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(28, 0, 28, 0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        row.Controls.Add(libraryCount, 0, 0);
        var pager = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        previousGames.Click += (_, _) => { libraryPageIndex--; FilterGames(); ScrollToLibrarySection(true); };
        nextGames.Click += (_, _) => { libraryPageIndex++; FilterGames(); ScrollToLibrarySection(true); };
        pager.Controls.Add(previousGames); pager.Controls.Add(pageNumber); pager.Controls.Add(nextGames); row.Controls.Add(pager, 1, 0);
        return row;
    }

    void BuildV2Home()
    {
        libraryPage.AutoScroll = true;
        games.AutoScroll = false;
        homeSection.Controls.Add(homeCards);
        homeOverview = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, BackColor = Base };
        homeOverview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); homeOverview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        homeOverview.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); homeOverview.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Control Stat(string title, Label value)
        {
            var card = new RoundedPanel { Dock = DockStyle.Fill, Radius = 2, BackColor = Surface, Padding = new Padding(18, 8, 18, 8), Margin = new Padding(0, 0, 16, 12) };
            card.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            value.Font = new Font(Font.FontFamily, 20, FontStyle.Bold); card.Controls.Add(value); return card;
        }
        homeOverview.Controls.Add(Stat("我的游戏", homeTotal), 0, 0); homeOverview.Controls.Add(Stat("已配置", homeConfigured), 1, 0);
        var add = Action("添加游戏 ↗", "Add game ↗", SelectGameManually); add.Dock = DockStyle.Fill; add.Radius = 2;
        var help = Action("使用帮助 ↗", "Help ↗", (_, _) => SwitchPage(4)); help.Dock = DockStyle.Fill; help.Radius = 2;
        homeOverview.Controls.Add(add, 0, 1); homeOverview.Controls.Add(help, 1, 1); homeSection.Controls.Add(homeOverview);
        scrollLibrary = Action("向下滚动查看全部游戏  ↓", "Scroll to all games ↓", (_, _) => SwitchPage(1));
        scrollLibrary.BackColor = Base; scrollLibrary.ForeColor = muted; homeSection.Controls.Add(scrollLibrary);
        libraryPage.Controls.Add(homeSection);
        libraryPage.ClientSizeChanged += (_, _) => LayoutV2Sections();
        libraryPage.Scroll += (_, _) => UpdateScrollNavigation();
        libraryPage.MouseWheel += (_, _) => BeginInvoke((Action)UpdateScrollNavigation);
        RenderHomeCards(); LayoutV2Sections();
    }

    void LayoutV2Sections()
    {
        if (layingOutSections || libraryRows == null || homeOverview == null) return;
        layingOutSections = true;
        try
        {
            int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
            var offset = -libraryPage.AutoScrollPosition.Y;
            var width = Math.Max(S(960), libraryPage.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            var cardWidth = Math.Max(S(100), (width - S(56) - 4 * S(24)) / 4);
            var cardHeight = (int)((cardWidth - S(6)) * 394d / 309d) + S(75);
            var homeHeight = Math.Max(libraryPage.ClientSize.Height, cardHeight + S(260));
            homeSection.SetBounds(0, -offset, width, homeHeight);
            homeCards.SetBounds(S(28), S(26), width - S(56), cardHeight + S(28));
            foreach (Control card in homeCards.Controls) SizeHomeCard(card, cardWidth, cardHeight);
            homeOverview.SetBounds(S(28), homeCards.Bottom + S(12), width - S(40), S(142));
            scrollLibrary.SetBounds((width - S(420)) / 2, homeOverview.Bottom + S(12), S(420), S(44));
            var libraryHeight = 2 * (cardHeight + S(24)) + S(230);
            libraryRows.SetBounds(0, homeHeight - offset, width, libraryHeight);
            libraryPage.AutoScrollMinSize = new Size(0, homeHeight + libraryHeight);
            LayoutGameCards();
        }
        finally { layingOutSections = false; }
    }

    void SizeHomeCard(Control card, int width, int height)
    {
        int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
        card.Size = new Size(width, height); card.Margin = new Padding(0, S(8), S(24), S(16));
        if (card.Controls.OfType<PictureBox>().FirstOrDefault() is not { } pic) return;
        pic.SetBounds(S(3), S(3), width - S(6), height - S(72));
        var labels = card.Controls.OfType<Label>().ToArray();
        if (labels.Length >= 2) { labels[0].SetBounds(S(12), pic.Bottom + S(10), width - S(24), S(25)); labels[1].SetBounds(S(12), pic.Bottom + S(37), width - S(24), S(24)); }
    }

    void RenderHomeCards()
    {
        if (homeOverview == null) return;
        if (selectedCard?.Parent == homeCards) selectedCard = null;
        while (homeCards.Controls.Count > 0) { var c = homeCards.Controls[0]; homeCards.Controls.Remove(c); c.Dispose(); }
        foreach (var game in libraryGames.Take(4)) homeCards.Controls.Add(CreateGameCard(game));
        // Empty slots are actions, never invented installed games.
        while (homeCards.Controls.Count < 4)
        {
            var add = new RoundedButton { Text = "+\n添加游戏", BackColor = Surface, ForeColor = Acid, Radius = 2, AccessibleName = "手动添加游戏" };
            add.Click += SelectGameManually; homeCards.Controls.Add(add);
        }
        LayoutV2Sections();
    }

    void ScrollToLibrarySection(bool library)
    {
        if (homeOverview == null) return;
        libraryPage.AutoScrollPosition = new Point(0, library ? homeSection.Height : 0);
        UpdateScrollNavigation();
    }

    void UpdateScrollNavigation()
    {
        if (activePage is not 0 and not 1) return;
        activePage = -libraryPage.AutoScrollPosition.Y >= homeSection.Height - 24 ? 1 : 0;
        for (var i = 0; i < navigation.Count; i++)
        {
            var b = (RoundedButton)navigation[i]; b.Active = navigationPages[i] == activePage;
            b.ForeColor = b.Active ? Acid : Muted; b.BackColor = b.Active ? SelectedSurface : Base; b.Invalidate();
        }
    }

    LibraryFilter GetLibraryState(GameCandidate game)
    {
        try
        {
            var record = GameManagement.Read(game.ExePath);
            if (record != null && record.Phase is not "installed" and not "restored") return LibraryFilter.Attention;
            return IsConfigured(game.ExePath) ? LibraryFilter.Configured : LibraryFilter.Unconfigured;
        }
        catch { return LibraryFilter.Attention; }
    }

    void SetLibraryFilter(LibraryFilter value)
    {
        libraryFilter = value; libraryPageIndex = 0;
        if (libraryAllTab != null) libraryAllTab.Selected = value == LibraryFilter.All;
        if (libraryConfiguredTab != null) libraryConfiguredTab.Selected = value == LibraryFilter.Configured;
        if (unconfiguredTab != null) unconfiguredTab.Selected = value == LibraryFilter.Unconfigured;
        if (attentionTab != null) attentionTab.Selected = value == LibraryFilter.Attention;
        FilterGames();
    }

    void RevealGame(GameCandidate game)
    {
        SetLibraryFilter(LibraryFilter.All);
        libraryPageIndex = Math.Max(0, libraryGames.IndexOf(game)) / LibraryPaging.PageSize;
        FilterGames();
    }

    void ShowGameConfiguration()
    {
        if (busy || selectedGame == null || configurationDialog != null) return;
        using var dialog = ProductDialog("游戏配置 · DLSS5", 800, 250);
        configurationDialog = dialog;
        dialog.Controls.Add(configurationSurface);
        dialog.FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        try { dialog.ShowDialog(this); }
        finally { dialog.Controls.Remove(configurationSurface); configurationDialog = null; }
    }
}
