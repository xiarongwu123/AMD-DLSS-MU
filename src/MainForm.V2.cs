using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    enum LibraryFilter { All, Configured, Unconfigured, Unsupported }
    LibraryFilter libraryFilter;
    readonly Dictionary<string, LibraryFilter> libraryStateCache = new(StringComparer.OrdinalIgnoreCase);
    TableLayoutPanel libraryRows = null!;
    readonly Panel homeSection = new AmbientCanvasPanel { BackColor = Base };
    ArtworkPanel homeHero = null!;
    Label homeSectionTitle = null!;
    Label homeSectionHint = null!;
    readonly FlowLayoutPanel homeCards = new() { WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
    readonly Label emptyLibrary = new() { AutoSize = false, Size = new Size(650, 100), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted };
    UnderlineTabButton? unconfiguredTab, attentionTab;
    ScrollHintControl scrollLibrary = null!;
    RoundedButton heroDlss = null!, heroMagpie = null!;
    bool layingOutSections;
    bool libraryOnly;
    Control? headerBrand, headerNavigation, headerSearch, headerUpdate;

    void SetHeaderMode(int page)
    {
        // Keep navigation available on every page, including Magpie.
        if (shellLayout != null) shellLayout.RowStyles[0].Height = (int)Math.Ceiling(88 * DeviceDpi / 96d);
        if (headerBrand != null) headerBrand.Visible = true;
        if (headerNavigation != null) headerNavigation.Visible = true;
        if (headerSearch != null) headerSearch.Visible = true;
        if (headerUpdate != null) headerUpdate.Visible = true;
    }

    Control BuildV2Navigation()
    {
        // Explicit geometry prevents WinForms' nested table layouts from cutting
        // the search field and update button off at the header/content boundary.
        var header = new PremiumHeaderPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var brand = new Panel { BackColor = Color.Transparent, Cursor = Cursors.Hand };
        var logo = new Panel { Dock = DockStyle.Left, Width = 68, BackColor = Color.Transparent, Cursor = Cursors.Hand, AccessibleName = "MU 首页" };
        Bitmap? logoArtwork = null;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.amd_dlss_mu_logo.png"))
            if (stream != null) { using var original = Image.FromStream(stream); logoArtwork = new Bitmap(original); }
        logo.Paint += (_, e) =>
        {
            if (logoArtwork == null) return;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(logoArtwork, new Rectangle(0, 7, 68, 40),
                new Rectangle((int)(logoArtwork.Width * .08), (int)(logoArtwork.Height * .11),
                    (int)(logoArtwork.Width * .87), (int)(logoArtwork.Height * .53)), GraphicsUnit.Pixel);
        };
        logo.Click += async (_, _) => await NavigateAuthorizedAsync(0); logo.Disposed += (_, _) => logoArtwork?.Dispose();
        var brandName = new Label { Text = "AMD DLSS MU", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Cursor = Cursors.Hand };
        brandName.Click += async (_, _) => await NavigateAuthorizedAsync(0);
        brand.Controls.Add(brandName); brand.Controls.Add(logo); header.Controls.Add(brand); headerBrand = brand;
        var nav = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
        foreach (var item in new[] { (0, "首页", "Home"), (1, "游戏库", "Library"), (7, "大力喜鹊", "Magpie"), (3, "下载任务", "Downloads"), (4, "帮助与反馈", "Help") })
        {
            var b = Action(item.Item2, item.Item3, async (_, _) =>
            {
                await NavigateAuthorizedAsync(item.Item1);
            });
            b.Size = new Size(item.Item1 == 4 ? 108 : 86, 42); b.Radius = 12; b.Margin = new Padding(0, 0, 7, 0);
            b.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            navigation.Add(b); navigationPages.Add(item.Item1); nav.Controls.Add(b);
        }
        header.Controls.Add(nav); headerNavigation = nav;
        var utility = new FlowLayoutPanel { WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Color.Transparent };
        themeToggle.Size = new Size(38, 34); themeToggle.Radius = 17; themeToggle.BackColor = Surface; themeToggle.ForeColor = Muted;
        themeToggle.Glyph = darkMode ? "\uE706" : "\uE708"; themeToggle.Text = ""; themeToggle.Margin = new Padding(0, 0, 7, 0);
        themeToggle.AccessibleName = "切换深色或浅色模式";
        themeToggle.Click += (_, _) => ToggleTheme(); utility.Controls.Add(themeToggle);
        var language = new RoundedButton { Size = new Size(54, 34), Text = english ? "EN" : "ZH", Radius = 17, BackColor = Surface, ForeColor = Ink, Margin = new Padding(0, 0, 7, 0) };
        language.Click += async (_, _) => { if (!await RequireFeatureAsync("configuration.edit")) return; english = !english; ApplyLanguage(english); language.Text = english ? "EN" : "ZH"; ApplyAccountGate(); }; utility.Controls.Add(language);
        var account = accountHeader = Action("登录 / 注册", "Account", async (_, _) => await NavigateAuthorizedAsync(6)); account.Size = new Size(104, 34); account.Radius = 17; account.Margin = new Padding(0, 0, 7, 0); utility.Controls.Add(account);
        var settings = Action("设置", "Settings", async (_, _) => await NavigateAuthorizedAsync(5)); settings.Size = new Size(60, 34); settings.Radius = 17; settings.Margin = new Padding(0, 0, 8, 0); utility.Controls.Add(settings);
        RoundedButton Caption(string symbol, EventHandler click)
        {
            var button = new RoundedButton { Text = symbol, Size = new Size(40, 34), Radius = 8, BackColor = Base, ForeColor = Muted, Margin = Padding.Empty, Cursor = Cursors.Hand, TabStop = false, Font = new Font("Segoe UI", 11f) };
            button.Click += click; return button;
        }
        utility.Controls.Add(Caption("—", (_, _) => WindowState = FormWindowState.Minimized));
        utility.Controls.Add(Caption("□", (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
        var close = Caption("×", (_, _) => Close()); utility.Controls.Add(close);
        header.Controls.Add(utility);
        var update = Action("检查更新 ↗", "Update ↗", async (_, _) => await CheckAppUpdateAsync(false));
        update.Margin = Padding.Empty; update.BackColor = Acid; update.ForeColor = OnAccent; update.Radius = 14; update.Glyph = "\uE72C";
        update.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        var search = new RoundedPanel { BackColor = Surface, BorderColor = Line, Radius = 13, Padding = new Padding(13, 9, 12, 6), Margin = Padding.Empty };
        var searchIcon = new Label { Text = "\uE721", Dock = DockStyle.Left, Width = 25, ForeColor = Muted, Font = new Font("Segoe MDL2 Assets", 12), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
        librarySearch.PlaceholderText = english ? "Search games..." : "搜索游戏 / Search games...";
        librarySearch.BackColor = Surface; librarySearch.ForeColor = ink;
        librarySearch.TextChanged += (_, _) => { if (accountClient.IsOnline && CanNavigateToPage(1)) { FilterGames(); if (activePage != 1) SwitchPage(1); } };
        search.Controls.Add(librarySearch); search.Controls.Add(searchIcon);
        header.Controls.Add(search); header.Controls.Add(update);
        headerSearch = search; headerUpdate = update;
        void LayoutHeader()
        {
            var width = header.ClientSize.Width;
            var scale = DeviceDpi / 96f;
            int S(int n) => (int)Math.Round(n * scale);
            brand.SetBounds(S(20), S(4), S(200), S(44));
            for (var i = 0; i < navigation.Count; i++)
            {
                navigation[i].Size = new Size(S(navigationPages[i] == 4 ? 108 : 86), S(38));
                navigation[i].Margin = new Padding(0, 0, S(7), 0);
            }
            nav.SetBounds(S(224), S(5), S(532), S(46));
            var utilityWidth = utility.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
            utility.SetBounds(width - utilityWidth - S(16), S(5), utilityWidth, S(38));
            search.SetBounds(width - S(420), S(45), S(244), S(35));
            update.SetBounds(width - S(164), S(45), S(148), S(35));
        }
        header.ClientSizeChanged += (_, _) => LayoutHeader();
        LayoutHeader();
        void BeginDrag(object? _, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragWindow(); }
        header.MouseDown += BeginDrag; brand.MouseDown += BeginDrag;
        header.DoubleClick += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        return header;
    }

    void BuildV2Home()
    {
        libraryPage.AutoScroll = true;
        games.AutoScroll = false;
        homeHero = new ArtworkPanel("AmdNrAssistant.mu-home-banner-new.png") { Radius = 18, ShowBorder = true, FadeBottom = true, BackColor = Color.FromArgb(6, 31, 27), FocusX = .5f };
        homeHero.Controls.Add(new HeroTitleControl { Font = new Font(Font.FontFamily, 34f, FontStyle.Bold), Name = "heroTitle" });
        heroDlss = Action("开启 DLSS5", "Open DLSS5", async (_, _) => await NavigateAuthorizedAsync(1));
        heroDlss.BackColor = Acid; heroDlss.ForeColor = OnAccent; heroDlss.Radius = 16;
        heroDlss.Glyph = "\uE768"; heroDlss.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        heroMagpie = Action("开启大力喜鹊", "Open Magpie", async (_, _) => await NavigateAuthorizedAsync(7));
        heroMagpie.BackColor = SelectedSurface; heroMagpie.ForeColor = Ink; heroMagpie.Radius = 16;
        heroMagpie.Glyph = "\uE945"; heroMagpie.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        homeHero.Controls.Add(heroDlss); homeHero.Controls.Add(heroMagpie);
        homeSection.Controls.Add(homeHero);
        homeSectionTitle = new Label { Text = "我的游戏", Font = new Font(Font.FontFamily, 18f, FontStyle.Bold), ForeColor = Ink, AutoSize = false, BackColor = Color.Transparent };
        homeSectionHint = new Label { Text = "已扫描到的本地游戏，选择并一键配置。", ForeColor = Muted, AutoSize = false, BackColor = Color.Transparent };
        homeSection.Controls.Add(homeSectionTitle); homeSection.Controls.Add(homeSectionHint);
        homeSection.Controls.Add(homeCards);
        scrollLibrary = new ScrollHintControl { Text = english ? "Scroll for all games" : "下滑查看全部游戏", ForeColor = Muted };
        scrollLibrary.Click += async (_, _) => await NavigateAuthorizedAsync(1);
        scrollLibrary.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold); homeSection.Controls.Add(scrollLibrary);
        libraryPage.Controls.Add(homeSection);
        libraryPage.ClientSizeChanged += (_, _) => LayoutV2Sections();
        libraryPage.Scroll += (_, _) => UpdateScrollNavigation();
        if (libraryPage is LibraryScrollPanel scrollHost)
            scrollHost.EnterLibraryRequested += (_, _) => { if (CanNavigateToPage(1)) AnimateToLibrary(); };
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
            var cardHeight = (int)((cardWidth - S(6)) * 0.88d) + S(102);
            var homeHeight = Math.Max(libraryPage.ClientSize.Height, S(294) + cardHeight + S(112));
            homeSection.SetBounds(0, libraryOnly ? 0 : -offset, width, homeHeight);
            homeHero.SetBounds(S(20), 0, width - S(40), S(245));
            homeHero.Controls["heroTitle"]?.SetBounds(S(42), S(35), homeHero.Width - S(84), S(82));
            heroDlss.SetBounds(S(45), S(139), S(196), S(52));
            heroMagpie.SetBounds(S(257), S(139), S(214), S(52));
            homeSectionTitle.SetBounds(S(27), S(251), S(180), S(42));
            homeSectionHint.SetBounds(S(207), S(258), width - S(430), S(35));
            scrollLibrary.SetBounds((width - S(250)) / 2, homeHeight - S(65), S(250), S(42));
            homeCards.SetBounds(S(20), S(294), width - S(40), cardHeight + S(26));
            foreach (Control card in homeCards.Controls) SizeHomeCard(card, cardWidth, cardHeight);
            var visible = games.Controls.Cast<Control>().Count(c => c.Visible);
            var libraryCardWidth = Math.Max(S(100), (width - S(56) - 5 * S(16)) / 5);
            var libraryCardHeight = (int)Math.Round((libraryCardWidth - S(6)) * 394d / 309d) + S(75);
            var rows = Math.Max(1, (visible + 4) / 5);
            var libraryHeight = S(142) + rows * (libraryCardHeight + S(22)) + S(24);
            libraryRows.SetBounds(0, libraryOnly ? -offset : homeHeight - offset, width, libraryHeight);
            libraryPage.AutoScrollMinSize = new Size(0, libraryOnly ? libraryHeight : homeHeight + libraryHeight);
            if (libraryPage is LibraryScrollPanel host) host.LibraryStart = libraryOnly ? 0 : homeHeight;
            LayoutGameCards();
        }
        finally { layingOutSections = false; }
    }

    void SizeHomeCard(Control card, int width, int height)
    {
        int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
        card.Size = new Size(width, height + S(9)); card.Margin = new Padding(0, 0, S(18), S(12));
        if (card is not HoverLiftSlot { Content: { } content }) return;
        if (content.Name == "addGameArtwork")
        {
            content.Controls["addTitle"]?.SetBounds(S(20), height - S(86), width - S(40), S(34));
            content.Controls["addHint"]?.SetBounds(S(20), height - S(48), width - S(40), S(26));
            return;
        }
        if (content.Controls.Find("addIcon", false).FirstOrDefault() is Control addIcon)
        {
            addIcon.SetBounds(0, Math.Max(0, height / 2 - S(52)), width, S(58));
            content.Controls.Find("addTitle", false).FirstOrDefault()?.SetBounds(0, height / 2 + S(9), width, S(45));
            return;
        }
        if (content.Controls.OfType<PictureBox>().FirstOrDefault() is not { } pic) return;
        var compact = width < S(280);
        // Reserve a fixed gutter for the largest hover stroke. Child windows
        // are always painted over their parent, including the parent's border.
        pic.SetBounds(S(6), S(6), width - S(12), height - (compact ? S(126) : S(88)) - S(4));
        if (pic is CoverPictureBox cover && content is RoundedPanel panel)
            cover.CornerRadius = Math.Max(1, panel.Radius - 6);
        if (content.Controls.OfType<Label>().FirstOrDefault() is { } title)
            title.SetBounds(S(14), pic.Bottom + S(8), width - S(28), S(26));
        if (content.Controls.OfType<StatusChip>().FirstOrDefault() is { } status)
            status.SetBounds(S(14), pic.Bottom + S(39), compact ? width - S(28) : width - S(183), S(34));
        if (content.Controls.Find("homeDetect", false).FirstOrDefault() is Control detect)
            detect.SetBounds(compact ? S(14) : width - S(166), compact ? pic.Bottom + S(77) : pic.Bottom + S(38),
                compact ? width - S(28) : S(150), S(37));
    }

    void RenderHomeCards()
    {
        if (homeHero == null) return;
        if (selectedCard?.Parent == homeCards) selectedCard = null;
        while (homeCards.Controls.Count > 0) { var c = homeCards.Controls[0]; homeCards.Controls.Remove(c); c.Dispose(); }
        foreach (var game in libraryGames.Take(3))
        {
            var card = new RoundedPanel { Radius = 18, BackColor = Surface, Cursor = Cursors.Hand, TabStop = true };
            Image image;
            if (game.CoverPath is { } coverPath && File.Exists(coverPath))
            { using var source = Image.FromFile(coverPath); image = new Bitmap(source); }
            else image = game.Icon?.ToBitmap() ?? SystemIcons.Application.ToBitmap();
            var cover = new CoverPictureBox { Image = image, BackColor = Surface, Tag = game.CoverPath != null,
                Cursor = Cursors.Hand, AccessibleName = game.Title };
            cover.Disposed += (_, _) => cover.Image?.Dispose();
            var name = new Label { Text = game.Title, BackColor = Color.Transparent, ForeColor = Ink,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), AutoEllipsis = true, Cursor = Cursors.Hand };
            var configured = false;
            try { configured = IsConfigured(game.ExePath); }
            catch { /* Show the safe unverified state for an unreadable game directory. */ }
            var state = new StatusChip { Text = configured ? "已配置 · 待验证" : "兼容性待检测" };
            var detect = Action("检测兼容性", "Check compatibility", async (_, _) =>
            {
                selectedGame = game.ExePath;
                await InspectSelectedAsync(false);
            });
            detect.Name = "homeDetect"; detect.Radius = 12; detect.BackColor = Acid; detect.ForeColor = OnAccent;
            detect.Glyph = "\uE721";
            detect.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            card.Controls.Add(cover); card.Controls.Add(name); card.Controls.Add(state); card.Controls.Add(detect);
            async void OpenGame(object? _, EventArgs __) { if (!await RequireFeatureAsync("library.manage")) return; SwitchPage(1); RevealGame(game); }
            card.Click += OpenGame; cover.Click += OpenGame; name.Click += OpenGame;
            card.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Space)
                { OpenGame(card, EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
            };
            homeCards.Controls.Add(new HoverLiftSlot { Content = card });
        }
        while (homeCards.Controls.Count < 3)
        {
            var blank = new RoundedPanel { BackColor = Surface, Radius = 18 };
            blank.Controls.Add(new Label { Name = "addIcon", Text = "\uE7FC", TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Acid, Font = new Font("Segoe MDL2 Assets", 26), BackColor = Color.Transparent });
            blank.Controls.Add(new Label { Name = "addTitle", Text = "等待扫描游戏", TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted, Font = new Font(Font.FontFamily, 11f), BackColor = Color.Transparent });
            homeCards.Controls.Add(new HoverLiftSlot { Content = blank });
        }
        var add = new ArtworkPanel("AmdNrAssistant.mu-add-game-art.png") { Name = "addGameArtwork", BackColor = Surface, Radius = 18, Cursor = Cursors.Hand,
            AccessibleName = "手动添加游戏", TabStop = true };
        var addHint = new Label { Name = "addHint", Text = "选择本地游戏 · 加入你的游戏库", ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        var addTitle = new Label { Name = "addTitle", Text = "添加游戏", ForeColor = Ink,
            Font = new Font(Font.FontFamily, 17f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent, Cursor = Cursors.Hand };
        add.Controls.Add(addHint); add.Controls.Add(addTitle);
        add.Click += SelectGameManually; addHint.Click += SelectGameManually; addTitle.Click += SelectGameManually;
        add.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) { SelectGameManually(add, EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; } };
        homeCards.Controls.Add(new HoverLiftSlot { Content = add });
        LayoutV2Sections();
    }

    void ScrollToLibrarySection(bool library)
    {
        if (homeHero == null) return;
        libraryPage.SuspendLayout();
        libraryPage.AutoScrollPosition = Point.Empty;
        libraryOnly = library;
        homeSection.Visible = !library;
        if (libraryPage is LibraryScrollPanel host) host.LockToLibrary = library;
        LayoutV2Sections();
        libraryPage.AutoScrollPosition = Point.Empty;
        libraryPage.ResumeLayout();
        UpdateScrollNavigation();
    }

    void AnimateToLibrary()
    {
        if (activePage == 1) return;
        // AutoScrollPosition uses ScrollWindow to move native child windows.
        // Driving it per animation frame tears their independently painted
        // surfaces. Use one settled page change; local hover motion remains.
        SwitchPage(1);
    }

    void UpdateScrollNavigation()
    {
        if (activePage is not 0 and not 1) return;
        if (!libraryOnly &&
            -libraryPage.AutoScrollPosition.Y >= homeSection.Height - 16)
        {
            ScrollToLibrarySection(true);
            return;
        }
        activePage = libraryOnly ? 1 : 0;
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
