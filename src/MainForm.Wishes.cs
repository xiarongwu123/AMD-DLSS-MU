using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Mu.Wishes;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel wishesPage = new BufferedPageHost { Dock = DockStyle.Fill, AutoScroll = true };
    readonly Panel wishCanvas = new() { BackColor = Color.Transparent };
    readonly FlowLayoutPanel wishRows = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
    readonly TextBox wishText = new() { Multiline = true, MaxLength = 500, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
    readonly TextBox wishSearch = new() { BorderStyle = BorderStyle.None, MaxLength = 100 };
    readonly ComboBox wishSort = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    readonly Label wishCounter = new() { Text = "0/500", TextAlign = ContentAlignment.MiddleRight };
    readonly Label wishFeedback = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label wishPagination = new() { TextAlign = ContentAlignment.MiddleCenter };
    readonly System.Windows.Forms.Timer wishSearchTimer = new() { Interval = 300 };
    readonly Dictionary<string, RoundedButton> wishFilters = new();
    ArtworkPanel wishHero = null!;
    WishComposerPanel wishComposer = null!;
    RoundedPanel wishInput = null!, wishSearchBox = null!;
    Panel wishToolbar = null!, wishFooter = null!;
    RoundedButton wishSubmit = null!, wishAttach = null!, wishPrevious = null!, wishNext = null!;
    string wishFilter = "all";
    string? wishImageData;
    WishSubmission? pendingWish;
    CancellationTokenSource? wishListCancellation;
    int wishPageNumber = 1, wishRequest;
    bool layingOutWishes, wishSubmitting;

    int WS(int value) => (int)Math.Round(value * DeviceDpi / 96d);
    Label WishLabel(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true,
        Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    void BuildWishesPage()
    {
        wishesPage.BackColor = Base;
        wishHero = new ArtworkPanel("AmdNrAssistant.mu-wish-pool-hero.png") { Radius = 18, ShowBorder = false, FocusY = .40f, BackColor = Base };
        var heroInk = Color.FromArgb(245, 248, 255);
        var title = WishLabel("许愿池", 30, heroInk, true); title.Name = "title";
        var beta = new RoundedButton { Text = "BETA", BackColor = Color.FromArgb(106, 218, 93), ForeColor = Color.FromArgb(8, 25, 13), Radius = 12, TabStop = false };
        beta.Name = "beta";
        var subtitle = WishLabel("把你想要的功能告诉我们", 17, heroInk, true); subtitle.Name = "subtitle";
        var hint = WishLabel("你的每一个建议都可能成为下一个版本的功能\n和大家一起，让 AMD DLSS MU 变得更好。", 10.5f, Color.FromArgb(174, 186, 205)); hint.Name = "hint";
        wishHero.Controls.AddRange(new Control[] { title, beta, subtitle, hint });
        foreach (var (text, name) in new[] { ("自动选择最佳方案", "bubble1"), ("支持更多游戏", "bubble2"), ("更好看的 UI", "bubble3") })
        {
            var bubble = new RoundedButton { Name = name, Text = text, BackColor = Color.FromArgb(37, 39, 79), ForeColor = Color.FromArgb(205, 211, 255), Radius = 14, TabStop = false, Font = new Font(Font.FontFamily, 9f) };
            wishHero.Controls.Add(bubble);
        }
        wishComposer = new WishComposerPanel { Radius = 18, BackColor = Surface };
        var write = WishLabel("✦  写下你的愿望", 16, Ink, true); write.Name = "write";
        var intro = WishLabel("告诉我们你希望增加的功能、改进建议或遇到的问题，我们会认真阅读每一条建议。", 10, Muted); intro.Name = "intro";
        wishComposer.Controls.AddRange(new Control[] { write, intro });
        wishInput = new RoundedPanel { Radius = 14, BackColor = Sidebar, BorderColor = Line };
        wishText.BackColor = Sidebar; wishText.ForeColor = Ink;
        wishText.PlaceholderText = "例如：希望增加自动选择最优渲染方案、支持某款游戏、优化 UI 界面、修复某个问题等…";
        wishText.AccessibleName = "愿望内容，最多 500 字";
        wishText.TextChanged += (_, _) => { wishCounter.Text = $"{wishText.Text.Length}/500"; pendingWish = null; };
        wishAttach = new RoundedButton { Text = "添加图片（可选）", Glyph = "\uEB9F", Radius = 8, BackColor = Sidebar, ForeColor = Muted, AccessibleName = "添加或移除愿望图片" };
        wishAttach.Click += (_, _) => AttachWishImage();
        wishCounter.BackColor = Sidebar; wishCounter.ForeColor = Muted;
        wishInput.Controls.AddRange(new Control[] { wishText, wishAttach, wishCounter });
        wishComposer.Controls.Add(wishInput);
        wishSubmit = new RoundedButton { Text = "提交愿望", Glyph = "\uE724", BackColor = Color.FromArgb(123, 230, 130), GradientEnd = Color.FromArgb(234, 240, 97), ForeColor = Color.FromArgb(10, 24, 10), Radius = 15, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold) };
        wishSubmit.Click += async (_, _) => await SubmitWishAsync();
        wishComposer.Controls.Add(wishSubmit);
        wishToolbar = new Panel { BackColor = Color.Transparent };
        foreach (var (key, label) in new[] { ("all", "全部"), ("hot", "热门"), ("planned", "已计划"), ("developing", "开发中"), ("completed", "已完成"), ("mine", "我的") })
        {
            var button = new RoundedButton { Text = label, AccessibleName = label + "愿望", BackColor = Surface, ForeColor = Muted, Radius = 12, Tag = label, Font = new Font(Font.FontFamily, 9f) };
            button.Click += async (_, _) => { wishFilter = key; wishPageNumber = 1; await LoadWishesAsync(); };
            wishFilters.Add(key, button); wishToolbar.Controls.Add(button);
        }
        wishSearchBox = new RoundedPanel { Radius = 12, BackColor = Surface, BorderColor = Line };
        wishSearch.BackColor = Surface; wishSearch.ForeColor = Ink; wishSearch.PlaceholderText = "搜索功能建议…";
        wishSearch.AccessibleName = "搜索愿望";
        wishSearch.TextChanged += (_, _) => { wishSearchTimer.Stop(); wishSearchTimer.Start(); };
        wishSearchBox.Controls.Add(wishSearch); wishToolbar.Controls.Add(wishSearchBox);
        wishSort.Items.AddRange(new[] { "最新发布", "最多支持" }); wishSort.SelectedIndex = 0;
        wishSort.BackColor = Surface; wishSort.ForeColor = Ink; wishSort.AccessibleName = "愿望排序";
        wishSort.SelectedIndexChanged += async (_, _) => { wishPageNumber = 1; if (activePage == 8) await LoadWishesAsync(); };
        wishToolbar.Controls.Add(wishSort);
        wishSearchTimer.Tick += async (_, _) => { wishSearchTimer.Stop(); wishPageNumber = 1; if (activePage == 8) await LoadWishesAsync(); };
        wishFooter = new Panel { BackColor = Color.Transparent };
        wishPrevious = new RoundedButton { Text = "上一页", BackColor = Surface, ForeColor = Ink, Radius = 10, Enabled = false };
        wishNext = new RoundedButton { Text = "下一页", BackColor = Surface, ForeColor = Ink, Radius = 10, Enabled = false };
        var refresh = new RoundedButton { Name = "refresh", Text = "刷新", Glyph = "\uE72C", BackColor = Surface, ForeColor = Muted, Radius = 10 };
        refresh.Click += async (_, _) => await LoadWishesAsync();
        wishPrevious.Click += async (_, _) => { wishPageNumber--; await LoadWishesAsync(); };
        wishNext.Click += async (_, _) => { wishPageNumber++; await LoadWishesAsync(); };
        wishPagination.ForeColor = Muted; wishPagination.BackColor = Color.Transparent;
        wishFeedback.ForeColor = Muted; wishFeedback.BackColor = Color.Transparent;
        wishFooter.Controls.AddRange(new Control[] { wishPrevious, wishNext, wishPagination, refresh });
        wishCanvas.Controls.AddRange(new Control[] { wishHero, wishComposer, wishToolbar, wishFeedback, wishRows, wishFooter });
        wishesPage.Controls.Add(wishCanvas); pageHost.Controls.Add(wishesPage);
        wishesPage.ClientSizeChanged += (_, _) => LayoutWishesPage();
        FormClosed += (_, _) => { wishSearchTimer.Stop(); wishSearchTimer.Dispose(); wishListCancellation?.Cancel(); wishListCancellation?.Dispose(); };
        LayoutWishesPage();
    }

    void LayoutWishesPage()
    {
        if (layingOutWishes || wishHero == null) return;
        layingOutWishes = true;
        try
        {
            var width = Math.Max(WS(1040), wishesPage.ClientSize.Width - WS(48) - SystemInformation.VerticalScrollBarWidth);
            var rowsHeight = wishRows.Controls.Count * WS(116);
            var height = WS(552) + rowsHeight + WS(64);
            var offset = wishesPage.AutoScrollPosition;
            wishCanvas.SetBounds(WS(24) + offset.X, WS(10) + offset.Y, width, height);
            wishHero.SetBounds(0, 0, width, WS(218));
            wishHero.Controls["title"]!.SetBounds(WS(8), WS(18), WS(160), WS(58));
            wishHero.Controls["beta"]!.SetBounds(WS(172), WS(33), WS(58), WS(28));
            wishHero.Controls["subtitle"]!.SetBounds(WS(10), WS(86), WS(470), WS(38));
            wishHero.Controls["hint"]!.SetBounds(WS(10), WS(135), WS(470), WS(60));
            wishHero.Controls["bubble1"]!.SetBounds(width * 49 / 100, WS(50), WS(148), WS(40));
            wishHero.Controls["bubble2"]!.SetBounds(width * 84 / 100, WS(10), WS(120), WS(36));
            wishHero.Controls["bubble3"]!.SetBounds(width - WS(146), WS(140), WS(125), WS(36));
            wishComposer.SetBounds(0, WS(220), width, WS(232));
            wishComposer.Controls["write"]!.SetBounds(WS(24), WS(19), width - WS(48), WS(34));
            wishComposer.Controls["intro"]!.SetBounds(WS(25), WS(60), width - WS(50), WS(26));
            wishInput.SetBounds(WS(24), WS(96), width - WS(258), WS(112));
            wishText.SetBounds(WS(14), WS(12), wishInput.Width - WS(28), WS(58));
            wishAttach.SetBounds(WS(8), WS(76), Math.Min(WS(440), wishInput.Width - WS(90)), WS(30));
            wishCounter.SetBounds(wishInput.Width - WS(82), WS(78), WS(66), WS(26));
            wishSubmit.SetBounds(width - WS(215), WS(146), WS(191), WS(62));
            wishToolbar.SetBounds(0, WS(474), width, WS(44));
            var x = 0;
            foreach (var button in wishFilters.Values) { button.SetBounds(x, 0, WS(86), WS(38)); x += WS(96); }
            wishSearchBox.SetBounds(WS(590), 0, width - WS(726), WS(38));
            wishSearch.SetBounds(WS(12), WS(9), wishSearchBox.Width - WS(24), WS(24));
            wishSort.SetBounds(width - WS(124), WS(4), WS(124), WS(32));
            wishFeedback.SetBounds(WS(3), WS(521), width - WS(6), WS(24));
            wishRows.SetBounds(0, WS(552), width, rowsHeight);
            foreach (Control row in wishRows.Controls) { row.Size = new Size(width, WS(108)); row.Margin = new Padding(0, 0, 0, WS(8)); }
            wishFooter.SetBounds(0, WS(552) + rowsHeight, width, WS(50));
            wishPrevious.SetBounds(width / 2 - WS(180), WS(6), WS(92), WS(36));
            wishPagination.SetBounds(width / 2 - WS(82), WS(6), WS(164), WS(36));
            wishNext.SetBounds(width / 2 + WS(88), WS(6), WS(92), WS(36));
            wishFooter.Controls["refresh"]!.SetBounds(width - WS(100), WS(6), WS(100), WS(36));
            wishesPage.AutoScrollMinSize = new Size(width + WS(48), height + WS(20));
        }
        finally { layingOutWishes = false; }
    }

    async Task LoadWishesAsync()
    {
        if (IsDisposed || !accountClient.CanKeepVerifiedView) return;
        wishListCancellation?.Cancel(); wishListCancellation?.Dispose();
        wishListCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = wishListCancellation.Token;
        var request = ++wishRequest;
        var owner = accountClient.Account?.Id;
        wishFeedback.Text = "正在从许愿池读取愿望…";
        wishPrevious.Enabled = wishNext.Enabled = false;
        foreach (var (key, button) in wishFilters)
        {
            button.Active = key == wishFilter; button.BackColor = key == wishFilter ? SelectedSurface : Surface;
            button.ForeColor = key == wishFilter ? Acid : Muted; button.Invalidate();
        }
        try
        {
            var result = await accountClient.GetWishesAsync(wishFilter, wishSearch.Text.Trim(), wishSort.SelectedIndex == 1 ? "votes" : "newest", wishPageNumber, token);
            if (IsDisposed || token.IsCancellationRequested || request != wishRequest || accountClient.Account?.Id != owner) return;
            ClearWishRows();
            foreach (var item in result.Items) wishRows.Controls.Add(BuildWishRow(item));
            var counts = new[] { result.Counts.All, result.Counts.Hot, result.Counts.Planned, result.Counts.Developing, result.Counts.Completed, result.Counts.Mine };
            var i = 0;
            foreach (var button in wishFilters.Values) button.Text = $"{button.Tag}  {counts[i++]}";
            var pages = Math.Max(1, (result.Total + result.PageSize - 1) / result.PageSize);
            wishPagination.Text = $"{result.Page} / {pages}  ·  {result.Total} 条";
            wishPrevious.Enabled = result.Page > 1; wishNext.Enabled = result.Page < pages;
            wishFeedback.Text = result.Items.Length == 0 ? "还没有找到愿望，试试其他关键词，或写下你的第一个愿望。"
                : result.Items.Any(w => w.IsDemo) ? "标有「示例」的内容为演示数据；欢迎提交你自己的愿望。" : "为你期待的功能投一票，让好想法被更多人看见。";
            LayoutWishesPage();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!IsDisposed && request == wishRequest)
            { ClearWishRows(); LayoutWishesPage(); wishFeedback.Text = "读取失败：" + e.Message + "  点击右下角刷新重试。"; }
        }
    }

    Control BuildWishRow(WishItem item)
    {
        var row = new RoundedPanel { Radius = 15, BackColor = Surface, BorderColor = Line };
        var vote = new RoundedButton { Text = $"⌃\n{item.Votes}", AccessibleName = (item.Voted ? "取消支持：" : "支持：") + item.Title,
            BackColor = item.Voted ? SelectedSurface : Sidebar, ForeColor = item.Voted ? Acid : Ink, Radius = 12,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), Active = item.Voted };
        vote.Click += async (_, _) =>
        {
            var owner = accountClient.Account?.Id;
            vote.Enabled = false;
            try { await accountClient.VoteWishAsync(item.Id, !item.Voted, lifetime.Token); if (!IsDisposed && accountClient.Account?.Id == owner) await LoadWishesAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) ShowProductToast(e.Message, false); }
            finally { if (!vote.IsDisposed) vote.Enabled = true; }
        };
        var title = WishLabel(item.Title, 12, Ink, true); title.Cursor = Cursors.Hand;
        var description = WishLabel(item.Content.Replace('\n', ' '), 9.5f, Muted); description.Cursor = Cursors.Hand;
        var tags = WishLabel("功能建议    " + (item.IsDemo ? "示例" : item.IsMine ? "我的愿望" : "社区愿望") + (item.HasImage ? "    ▧ 附图" : ""), 8.5f, WishTagColor); tags.Name = "wishTags";
        var status = new RoundedButton { Name = "wishStatus", Tag = item.Status, Text = WishStatus(item.Status), BackColor = SelectedSurface, ForeColor = WishStatusColor(item.Status), Radius = 8, TabStop = false, Font = new Font(Font.FontFamily, 9f) };
        var discussion = new RoundedButton { Text = $"评论 {item.Comments}", Glyph = "\uE8F2", BackColor = Surface, ForeColor = Muted, Radius = 8, AccessibleName = "查看愿望和评论：" + item.Title };
        var author = WishLabel(item.Author, 9, Muted);
        var date = WishLabel(DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt).ToLocalTime().ToString("yyyy-MM-dd"), 8, Muted);
        async void Open(object? sender, EventArgs e) => await OpenWishAsync(item.Id);
        title.Click += Open; description.Click += Open; discussion.Click += Open;
        row.Controls.AddRange(new Control[] { vote, title, description, tags, status, discussion, author, date });
        row.ClientSizeChanged += (_, _) =>
        {
            vote.SetBounds(WS(8), WS(12), WS(68), WS(84));
            var contentWidth = row.Width - WS(410);
            title.SetBounds(WS(112), WS(9), contentWidth, WS(28));
            description.SetBounds(WS(112), WS(39), contentWidth, WS(38));
            tags.SetBounds(WS(112), WS(80), contentWidth, WS(22));
            status.SetBounds(row.Width - WS(274), WS(20), WS(88), WS(30));
            discussion.SetBounds(row.Width - WS(162), WS(10), WS(144), WS(30));
            author.SetBounds(row.Width - WS(151), WS(47), WS(135), WS(24));
            date.SetBounds(row.Width - WS(151), WS(77), WS(135), WS(22));
        };
        return row;
    }

    static string WishStatus(string status) => status switch { "planned" => "♧ 已计划", "developing" => "开发中", "completed" => "✓ 已完成", _ => "待评估" };
    static Color WishTagColor => IsDarkTheme ? Color.FromArgb(109, 177, 234) : Color.FromArgb(34, 97, 149);
    static Color WishStatusColor(string status) => status switch
    {
        "planned" => IsDarkTheme ? Color.FromArgb(89, 224, 115) : Color.FromArgb(22, 112, 50),
        "developing" => IsDarkTheme ? Color.FromArgb(110, 164, 255) : Color.FromArgb(40, 93, 184),
        "completed" => IsDarkTheme ? Color.FromArgb(183, 231, 99) : Color.FromArgb(70, 110, 14), _ => Muted
    };

    void RefreshWishTheme()
    {
        foreach (Control row in wishRows.Controls)
        {
            if (row.Controls["wishStatus"] is { Tag: string status } chip) chip.ForeColor = WishStatusColor(status);
            if (row.Controls["wishTags"] is { } tags) tags.ForeColor = WishTagColor;
        }
    }

    void ClearWishRows()
    {
        while (wishRows.Controls.Count > 0) { var row = wishRows.Controls[0]; wishRows.Controls.Remove(row); row.Dispose(); }
    }

    void ClearWishesAccess()
    {
        wishRequest++; wishListCancellation?.Cancel(); wishSearchTimer.Stop(); ClearWishRows();
        wishText.Clear(); wishSearch.Clear(); wishImageData = null; pendingWish = null; wishAttach.Text = "添加图片（可选）";
        wishFeedback.Text = "登录后查看许愿池。";
        foreach (var button in wishFilters.Values) button.Text = button.Tag?.ToString();
        wishPagination.Text = ""; wishPrevious.Enabled = wishNext.Enabled = false;
        LayoutWishesPage();
    }

    void AttachWishImage()
    {
        if (wishImageData != null) { wishImageData = null; pendingWish = null; wishAttach.Text = "添加图片（可选）"; return; }
        using var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg", Title = "添加愿望图片" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024) throw new IOException("请选择 10 MB 以内的图片。");
            using var original = Image.FromFile(dialog.FileName);
            if ((long)original.Width * original.Height > 40000000) throw new IOException("图片尺寸过大，请先缩小图片。");
            var scale = Math.Min(1d, 960d / Math.Max(original.Width, original.Height));
            using var resized = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
            using (var graphics = Graphics.FromImage(resized))
            { graphics.Clear(Color.FromArgb(12, 17, 26)); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(original, new Rectangle(0, 0, resized.Width, resized.Height)); }
            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
            resized.Save(output, ImageCodecInfo.GetImageEncoders().Single(x => x.MimeType == "image/jpeg"), parameters);
            if (output.Length > 250000) throw new IOException("图片压缩后仍过大，请选择较简单的图片。");
            wishImageData = "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray()); pendingWish = null;
            wishAttach.Text = "已添加 " + Path.GetFileName(dialog.FileName) + "  × 移除";
        }
        catch (Exception e) { ShowProductToast("添加图片失败：" + e.Message, false); }
    }

    async Task SubmitWishAsync()
    {
        if (wishSubmitting) return;
        var content = wishText.Text.Trim();
        if (content.Length < 5) { ShowProductToast("请用至少 5 个字描述你的愿望。", false); wishText.Focus(); return; }
        pendingWish ??= new WishSubmission(Guid.NewGuid().ToString("N"), content, wishImageData);
        var submission = pendingWish;
        var owner = accountClient.Account?.Id;
        wishSubmitting = true; wishSubmit.Enabled = wishText.Enabled = wishAttach.Enabled = false;
        wishSubmit.Text = "正在提交…";
        try
        {
            await accountClient.SubmitWishAsync(submission, lifetime.Token);
            if (IsDisposed || accountClient.Account?.Id != owner) return;
            wishText.Clear(); wishImageData = null; pendingWish = null; wishAttach.Text = "添加图片（可选）";
            wishFilter = "mine"; wishPageNumber = 1; wishSearch.Clear();
            ShowProductToast("愿望已投入许愿池，谢谢你的建议！", true);
            await LoadWishesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) ShowProductToast("提交失败：" + e.Message, false); }
        finally
        {
            wishSubmitting = false;
            if (!IsDisposed) { wishSubmit.Enabled = wishText.Enabled = wishAttach.Enabled = true; wishSubmit.Text = "提交愿望"; }
        }
    }

    async Task OpenWishAsync(string id)
    {
        var owner = accountClient.Account?.Id;
        try
        {
            var detail = await accountClient.GetWishAsync(id, lifetime.Token);
            if (IsDisposed || accountClient.Account?.Id != owner) return;
            using var dialog = new Form { Text = "愿望详情", Size = new Size(800, 720), MinimumSize = new Size(640, 580),
                StartPosition = FormStartPosition.CenterParent, Font = Font, AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi };
            ThemeWindow(dialog);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top };
            var title = WishLabel(detail.Wish.Title, 18, Ink, true); title.Size = new Size(690, 42);
            var info = WishLabel($"{WishStatus(detail.Wish.Status)}  ·  {detail.Wish.Author}  ·  {detail.Wish.Votes} 人支持" + (detail.Wish.IsDemo ? "  ·  示例" : ""), 10, Muted); info.Size = new Size(690, 30);
            var content = new Label { Text = detail.Wish.Content, AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Ink, BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 16) };
            stack.Controls.AddRange(new Control[] { title, info, content });
            PictureBox? picture = null;
            if (detail.ImageData != null)
            {
                var bytes = Convert.FromBase64String(detail.ImageData[(detail.ImageData.IndexOf(',') + 1)..]);
                using var stream = new MemoryStream(bytes); using var decoded = Image.FromStream(stream);
                picture = new PictureBox { Image = new Bitmap(decoded), Size = new Size(690, 260), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Sidebar };
                picture.Disposed += (_, _) => picture.Image?.Dispose(); stack.Controls.Add(picture);
            }
            var commentsTitle = WishLabel($"讨论 · {detail.Wish.Comments}", 13, Ink, true); commentsTitle.Size = new Size(690, 34); stack.Controls.Add(commentsTitle);
            void AddComment(WishComment c)
            {
                var label = new Label { Text = $"{c.Author}  ·  {DateTimeOffset.FromUnixTimeSeconds(c.CreatedAt).ToLocalTime():MM-dd HH:mm}\n{c.Content}",
                    AutoSize = true, MaximumSize = new Size(Math.Max(200, scroll.ClientSize.Width - 28), 0), ForeColor = Muted, Margin = new Padding(0, 8, 0, 12) };
                stack.Controls.Add(label);
            }
            foreach (var comment in detail.Comments) AddComment(comment);
            scroll.Controls.Add(stack); layout.Controls.Add(scroll, 0, 0);
            var editor = new TextBox { Multiline = true, Dock = DockStyle.Fill, MaxLength = 300, PlaceholderText = "说说你的想法（最多 300 字）", BackColor = Sidebar, ForeColor = Ink };
            layout.Controls.Add(editor, 0, 1);
            var detailCommentCount = detail.Wish.Comments;
            var send = new RoundedButton { Text = "发表评论", Dock = DockStyle.Right, Width = 150, BackColor = Acid, ForeColor = OnAccent, Radius = 10 };
            send.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(editor.Text)) { editor.Focus(); return; }
                send.Enabled = editor.Enabled = false;
                try
                {
                    var comment = await accountClient.CommentWishAsync(id, editor.Text.Trim(), lifetime.Token);
                    if (dialog.IsDisposed || accountClient.Account?.Id != owner) return;
                    AddComment(comment); editor.Clear(); commentsTitle.Text = "讨论 · " + (++detailCommentCount);
                    scroll.ScrollControlIntoView(stack.Controls[stack.Controls.Count - 1]);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (!dialog.IsDisposed) MessageBox.Show(dialog, e.Message, "评论失败"); }
                finally { if (!dialog.IsDisposed) send.Enabled = editor.Enabled = true; }
            };
            layout.Controls.Add(send, 0, 2); dialog.Controls.Add(layout);
            scroll.ClientSizeChanged += (_, _) =>
            {
                var width = Math.Max(200, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
                foreach (Control child in stack.Controls)
                {
                    child.MaximumSize = new Size(width, 0);
                    if (!child.AutoSize) child.Width = width;
                }
            };
            dialog.ShowDialog(this);
            if (!IsDisposed && accountClient.Account?.Id == owner) await LoadWishesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) ShowProductToast("读取愿望失败：" + e.Message, false); }
    }
}

sealed class WishComposerPanel : RoundedPanel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
        using var path = UiPaint.RoundPath(bounds, Radius * DeviceDpi / 96f);
        var top = MainForm.IsDarkTheme ? Color.FromArgb(22, 28, 46) : Color.FromArgb(237, 244, 250);
        using var fill = new LinearGradientBrush(ClientRectangle, top, MainForm.Surface, 90f);
        e.Graphics.FillPath(fill, path);
        using var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(58, 160, 186), Color.FromArgb(132, 146, 57), 12f);
        using var border = new Pen(gradient, 1.2f);
        e.Graphics.DrawPath(border, path);
    }
}
