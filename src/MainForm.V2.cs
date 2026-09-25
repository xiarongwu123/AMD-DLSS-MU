using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    enum LibraryFilter { All, Configured, Unconfigured, Unsupported }
    LibraryFilter libraryFilter;
    readonly Dictionary<string, LibraryFilter> libraryStateCache = new(StringComparer.OrdinalIgnoreCase);
    TableLayoutPanel libraryRows = null!;
    RoundedPanel configurationSurface = null!;
    readonly Panel homeSection = new HudPanel { BackColor = Base };
    ArtworkPanel homeHero = null!;
    Label homeSectionTitle = null!;
    Label homeSectionHint = null!;
    readonly FlowLayoutPanel homeCards = new() { WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
    readonly Label emptyLibrary = new() { AutoSize = false, Size = new Size(650, 100), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted };
    UnderlineTabButton? unconfiguredTab, attentionTab;
    RoundedButton scrollLibrary = null!;
    System.Windows.Forms.Timer? sectionTransition;
    bool layingOutSections;

    Control BuildV2Navigation()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Base, Padding = new Padding(24, 8, 24, 8), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var brand = new Panel { Dock = DockStyle.Fill, BackColor = Base };
        var logo = new PictureBox { Dock = DockStyle.Left, Width = 86, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand, AccessibleName = "MU 首页" };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.amd_dlss_mu_logo.png"))
            if (stream != null) { using var original = Image.FromStream(stream); logo.Image = new Bitmap(original); }
        logo.Click += (_, _) => SwitchPage(0); logo.Disposed += (_, _) => logo.Image?.Dispose();
        brand.Controls.Add(new Label { Text = "AMD DLSS MU", Dock = DockStyle.Fill, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold) });
        brand.Controls.Add(logo); header.Controls.Add(brand, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var item in new[] { (0, "首页", "Home"), (1, "游戏库", "Library"), (7, "大力喜鹊", "Magpie"), (3, "下载任务", "Downloads"), (4, "帮助与反馈", "Help") })
        {
            var b = Action(item.Item2, item.Item3, (_, _) => SwitchPage(item.Item1));
            b.Size = new Size(item.Item1 == 4 ? 132 : 110, 50); b.Radius = 13; b.Margin = new Padding(0, 0, 7, 0);
            navigation.Add(b); navigationPages.Add(item.Item1); nav.Controls.Add(b);
        }
        header.Controls.Add(nav, 1, 0);
        var search = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Surface, Radius = 18, Padding = new Padding(12, 15, 12, 10), Margin = new Padding(0, 0, 12, 0) };
        librarySearch.BackColor = Surface; librarySearch.ForeColor = ink;
        librarySearch.TextChanged += (_, _) => { FilterGames(); SwitchPage(1); };
        search.Controls.Add(librarySearch); header.Controls.Add(search, 2, 0);
        var update = Action("检查更新 ↗", "Update ↗", async (_, _) => await CheckAppUpdateAsync(false));
        update.Dock = DockStyle.Fill; update.BackColor = Acid; update.ForeColor = OnAccent; update.Radius = 18;
        header.Controls.Add(update, 3, 0);
        header.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
        return header;
    }

    void BuildV2Home()
    {
        libraryPage.AutoScroll = true;
        games.AutoScroll = false;
        homeHero = new ArtworkPanel("AmdNrAssistant.mu-hero-art.png") { Radius = 24, BackColor = Color.FromArgb(6, 31, 27), BorderColor = Color.FromArgb(28, 120, 66) };
        homeHero.Controls.Add(new Label { Text = "一键配置 AMD DLSS5", Font = new Font(Font.FontFamily, 34f, FontStyle.Bold), ForeColor = Ink, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Name = "heroTitle" });
        homeHero.Controls.Add(new Label { Text = "自动检测本地游戏，下载并校验所需组件。实际效果需进入游戏验证。", Font = new Font(Font.FontFamily, 11f), ForeColor = Muted, BackColor = Color.Transparent, AutoSize = false, Name = "heroSummary" });
        homeSection.Controls.Add(homeHero);
        homeSectionTitle = new Label { Text = "我的游戏", Font = new Font(Font.FontFamily, 18f, FontStyle.Bold), ForeColor = Ink, AutoSize = false };
        homeSectionHint = new Label { Text = "已扫描到的本地游戏，选择并一键配置。", ForeColor = Muted, AutoSize = false };
        homeSection.Controls.Add(homeSectionTitle); homeSection.Controls.Add(homeSectionHint);
        homeSection.Controls.Add(homeCards);
        scrollLibrary = Action("查看全部游戏  →", "All games →", (_, _) => SwitchPage(1));
        scrollLibrary.BackColor = Surface; scrollLibrary.ForeColor = Acid; scrollLibrary.Radius = 16; homeSection.Controls.Add(scrollLibrary);
        libraryPage.Controls.Add(homeSection);
        libraryPage.ClientSizeChanged += (_, _) => LayoutV2Sections();
        libraryPage.Scroll += (_, _) => UpdateScrollNavigation();
        if (libraryPage is LibraryScrollPanel scrollHost)
            scrollHost.EnterLibraryRequested += (_, _) => AnimateToLibrary();
        RenderHomeCards(); LayoutV2Sections();
    }

    void LayoutV2Sections()
    {
        if (layingOutSections || libraryRows == null || homeHero == null) return;
        layingOutSections = true;
        try
        {
            int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
            var offset = -libraryPage.AutoScrollPosition.Y;
            var width = Math.Max(S(960), libraryPage.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            var cardWidth = Math.Max(S(100), (width - S(56) - 3 * S(18)) / 4);
            var cardHeight = (int)((cardWidth - S(6)) * 0.82d) + S(75);
            var homeHeight = Math.Max(libraryPage.ClientSize.Height, S(370) + cardHeight + S(100));
            homeSection.SetBounds(0, -offset, width, homeHeight);
            homeHero.SetBounds(S(28), S(22), width - S(56), S(284));
            homeHero.Controls["heroTitle"]?.SetBounds(S(42), S(31), homeHero.Width - S(84), S(82));
            homeHero.Controls["heroSummary"]?.SetBounds(S(44), S(116), Math.Min(homeHero.Width - S(88), S(700)), S(66));
            homeSectionTitle.SetBounds(S(30), S(321), S(180), S(42));
            homeSectionHint.SetBounds(S(210), S(327), width - S(430), S(35));
            scrollLibrary.SetBounds(width - S(215), S(321), S(180), S(42));
            homeCards.SetBounds(S(28), S(370), width - S(56), cardHeight + S(28));
            foreach (Control card in homeCards.Controls) SizeHomeCard(card, cardWidth, cardHeight);
            var visible = games.Controls.Cast<Control>().Count(c => c.Visible);
            var libraryCardWidth = Math.Max(S(100), (width - S(56) - 5 * S(16)) / 5);
            var libraryCardHeight = (int)Math.Round((libraryCardWidth - S(6)) * 394d / 309d) + S(75);
            var rows = Math.Max(1, (visible + 4) / 5);
            var libraryHeight = S(142) + rows * (libraryCardHeight + S(22)) + S(166);
            libraryRows.SetBounds(0, homeHeight - offset, width, libraryHeight);
            libraryPage.AutoScrollMinSize = new Size(0, homeHeight + libraryHeight);
            if (libraryPage is LibraryScrollPanel host) host.LibraryStart = homeHeight;
            LayoutGameCards();
        }
        finally { layingOutSections = false; }
    }

    void SizeHomeCard(Control card, int width, int height)
    {
        int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
        card.Size = new Size(width, height); card.Margin = new Padding(0, S(8), S(18), S(16));
        if (card.Controls.OfType<PictureBox>().FirstOrDefault() is not { } pic) return;
        pic.SetBounds(S(3), S(3), width - S(6), height - S(72));
        var labels = card.Controls.OfType<Label>().ToArray();
        if (labels.Length >= 2) { labels[0].SetBounds(S(12), pic.Bottom + S(10), width - S(24), S(25)); labels[1].SetBounds(S(12), pic.Bottom + S(37), width - S(24), S(24)); }
    }

    void RenderHomeCards()
    {
        if (homeHero == null) return;
        if (selectedCard?.Parent == homeCards) selectedCard = null;
        while (homeCards.Controls.Count > 0) { var c = homeCards.Controls[0]; homeCards.Controls.Remove(c); c.Dispose(); }
        foreach (var game in libraryGames.Take(3)) homeCards.Controls.Add(CreateGameCard(game));
        while (homeCards.Controls.Count < 3)
        {
            var blank = new RoundedPanel { BackColor = Surface, Radius = 18 };
            homeCards.Controls.Add(blank);
        }
        var add = new RoundedButton { Text = "+\n添加游戏", BackColor = Surface, ForeColor = Acid, Radius = 18, Font = new Font(Font.FontFamily, 17f, FontStyle.Bold), AccessibleName = "手动添加游戏" };
        add.Click += SelectGameManually; homeCards.Controls.Add(add);
        LayoutV2Sections();
    }

    void ScrollToLibrarySection(bool library)
    {
        if (homeHero == null) return;
        if (libraryPage is LibraryScrollPanel host) host.LockToLibrary = library;
        libraryPage.AutoScrollPosition = new Point(0, library ? homeSection.Height : 0);
        UpdateScrollNavigation();
    }

    void AnimateToLibrary()
    {
        if (sectionTransition?.Enabled == true || activePage == 1) return;
        var origin = -libraryPage.AutoScrollPosition.Y;
        var target = homeSection.Height;
        var frame = 0;
        sectionTransition = new System.Windows.Forms.Timer { Interval = 15 };
        sectionTransition.Tick += (_, _) =>
        {
            frame++;
            var t = Math.Min(1d, frame / 22d);
            var eased = 1d - Math.Pow(1d - t, 3d);
            libraryPage.AutoScrollPosition = new Point(0, origin + (int)Math.Round((target - origin) * eased));
            if (t < 1d) return;
            sectionTransition!.Stop(); sectionTransition.Dispose(); sectionTransition = null;
            if (libraryPage is LibraryScrollPanel host) host.LockToLibrary = true;
            activePage = 1; UpdateScrollNavigation();
        };
        sectionTransition.Start();
    }

    void UpdateScrollNavigation()
    {
        if (activePage is not 0 and not 1) return;
        activePage = libraryPage is LibraryScrollPanel { LockToLibrary: true } || -libraryPage.AutoScrollPosition.Y >= homeSection.Height - 24 ? 1 : 0;
        for (var i = 0; i < navigation.Count; i++)
        {
            var b = (RoundedButton)navigation[i]; b.Active = navigationPages[i] == activePage;
            b.ForeColor = b.Active ? Acid : Muted; b.BackColor = b.Active ? SelectedSurface : Base; b.Invalidate();
        }
    }

    LibraryFilter GetLibraryState(GameCandidate game)
    {
        if (libraryStateCache.TryGetValue(game.ExePath, out var cached)) return cached;
        try
        {
            var record = GameManagement.Read(game.ExePath);
            var state = record is { Phase: not "installed" and not "restored" } ? LibraryFilter.Unsupported
                : IsConfigured(game.ExePath) ? LibraryFilter.Configured
                : GameManagement.Check(game.ExePath).Blocked ? LibraryFilter.Unsupported : LibraryFilter.Unconfigured;
            libraryStateCache[game.ExePath] = state;
            return state;
        }
        catch { libraryStateCache[game.ExePath] = LibraryFilter.Unsupported; return LibraryFilter.Unsupported; }
    }

    void SetLibraryFilter(LibraryFilter value)
    {
        libraryFilter = value;
        if (libraryAllTab != null) libraryAllTab.Selected = value == LibraryFilter.All;
        if (libraryConfiguredTab != null) libraryConfiguredTab.Selected = value == LibraryFilter.Configured;
        if (unconfiguredTab != null) unconfiguredTab.Selected = value == LibraryFilter.Unconfigured;
        if (attentionTab != null) attentionTab.Selected = value == LibraryFilter.Unsupported;
        FilterGames();
    }

    void RevealGame(GameCandidate game)
    {
        SetLibraryFilter(LibraryFilter.All);
        FilterGames();
    }
}
