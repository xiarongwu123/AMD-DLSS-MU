using System.Diagnostics;
using System.Net.Mail;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel accountPage = new HudPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Base };
    readonly Dictionary<string, DateTimeOffset> codeCooldowns = new(StringComparer.OrdinalIgnoreCase);
    string accountMode = "login";
    bool keepAccountLogin = true;

    void BuildAccountPage() { pageHost.Controls.Add(accountPage); InitializeAccountAccess(); RenderAccount(); }

    async Task RunAccountActionAsync(Func<Task> action, string pending)
    {
        if (accountRequestBusy) return;
        accountRequestBusy = true; accountPage.Enabled = false; status.Text = pending;
        try { await action(); }
        catch (Exception e) { accountFeedback = AccountError(e); }
        finally
        {
            accountRequestBusy = false;
            if (!IsDisposed) { accountPage.Enabled = true; RenderAccount(); ApplyAccountGate(); status.Text = accountFeedback; }
        }
    }

    void RenderAccount()
    {
        if (IsDisposed) return;
        while (accountPage.Controls.Count > 0) { var c = accountPage.Controls[0]; accountPage.Controls.Remove(c); c.Dispose(); }
        translations.RemoveAll(item => item.control.IsDisposed);
        bool signedIn = accountClient.Account != null && accountClient.HasSession;
        bool register = accountMode == "register" && !signedIn;
        bool reset = accountMode == "reset" && !signedIn;
        bool change = accountMode == "password" && signedIn;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        var background = new ArtworkPanel("AmdNrAssistant.mu-account-art.png") { BackColor = Base, Radius = 1, ShowBorder = false, FocusX = .5f };
        var headline = new Label { Text = "MU 通行证\n连接你的游戏体验", ForeColor = Ink, Font = new Font(Font.FontFamily, 29, FontStyle.Bold), BackColor = Color.Transparent };
        var description = new Label { Text = "登录 MU 账户后使用客户端功能。\n\n账户安全与会员状态统一管理。\n客户端需联网验证登录与功能权限。\n\nPro 付费服务暂未开放。", ForeColor = Muted, Font = new Font(Font.FontFamily, 12), BackColor = Color.Transparent };
        background.Controls.Add(headline); background.Controls.Add(description);
        var panel = new GlassPanel { BackColor = Color.Transparent, Radius = 22, Padding = Padding.Empty };
        var form = new TableLayoutPanel { AutoSize = false, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty, BackColor = Color.Transparent };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int formHeight = 0;
        var rowHeights = new List<int>();
        void Row(Control control, int height)
        {
            rowHeights.Add(height);
            height = S(height); formHeight += height;
            var row = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Margin = new Padding(0, 0, 0, S(10));
            if (control is Label or CheckBox or FlowLayoutPanel) control.BackColor = Color.Transparent;
            if (control is Label && control.ForeColor == Control.DefaultForeColor) control.ForeColor = Ink;
            control.Dock = DockStyle.Fill; form.Controls.Add(control, 0, row);
        }
        TextBox Field(string label, bool secret = false, int max = 254)
        {
            Row(new Label { Text = label, ForeColor = Muted, TextAlign = ContentAlignment.TopLeft, Font = new Font(Font.FontFamily, 9f) }, 25);
            var border = new RoundedPanel { BackColor = Base, Radius = 14, BorderColor = Line, Padding = new Padding(S(14), S(13), S(12), S(8)) };
            var input = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Base, ForeColor = Ink, UseSystemPasswordChar = secret, AccessibleName = label, MaxLength = max,
                PlaceholderText = secret ? "至少 8 位字符" : label.Contains("验证码") ? "输入 6 位验证码" : "name@example.com" };
            border.Controls.Add(input);
            var icon = new Label { Text = secret ? "\uE72E" : label.Contains("验证码") ? "\uE73E" : "\uE715", Dock = DockStyle.Left, Width = S(30), BackColor = Color.Transparent,
                ForeColor = Muted, Font = new Font("Segoe MDL2 Assets", 13), TextAlign = ContentAlignment.TopLeft };
            border.Controls.Add(icon);
            if (secret)
            {
                var show = new RoundedButton { Text = "", Glyph = "\uE890", AccessibleName = "显示密码", Dock = DockStyle.Right, Width = S(32), Radius = 8, BackColor = Base, ForeColor = Muted };
                show.Click += (_, _) => { input.UseSystemPasswordChar = !input.UseSystemPasswordChar; show.Glyph = input.UseSystemPasswordChar ? "\uE890" : "\uED1A"; show.AccessibleName = input.UseSystemPasswordChar ? "显示密码" : "隐藏密码"; };
                border.Controls.Add(show);
            }
            Row(border, 55); return input;
        }
        void ButtonRow(string title, EventHandler action, bool primary = false)
        {
            var button = Action(title, title, action); button.Radius = 14; button.Font = new Font(Font.FontFamily, 11f, primary ? FontStyle.Bold : FontStyle.Regular);
            if (primary) { button.BackColor = Acid; button.ForeColor = OnAccent; }
            Row(button, primary ? 62 : 54);
        }
        void LinkRow(string title, EventHandler action)
        {
            var link = new LinkLabel { Text = title, LinkColor = Muted, ActiveLinkColor = Acid, VisitedLinkColor = Muted,
                LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            link.LinkClicked += (_, e) => action(link, e); Row(link, 38);
        }
        void Mode(string mode) { accountMode = mode; accountFeedback = ""; RenderAccount(); }
        var feedback = new Label { Text = accountFeedback, ForeColor = Acid, AutoEllipsis = false };
        bool ValidateEmail(TextBox email)
        {
            var value = email.Text.Trim();
            if (MailAddress.TryCreate(value, out var address) && address.Address == value) return true;
            feedback.Text = "请输入有效的邮箱地址。"; email.Focus(); return false;
        }
        bool ValidatePassword(TextBox password, TextBox? confirmation)
        {
            if (password.Text.Length < 8) { feedback.Text = "密码至少需要 8 位字符。"; password.Focus(); return false; }
            if (confirmation != null && confirmation.Text != password.Text) { feedback.Text = "两次输入的密码不一致。"; confirmation.Focus(); return false; }
            return true;
        }
        var title = signedIn ? change ? "修改密码" : "我的账户" : register ? "创建 MU 账户" : reset ? "找回密码" : "登录 MU 账户";
        if (!signedIn && !reset)
        {
            var tabs = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
            tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            foreach (var item in new[] { ("登录", "login"), ("注册", "register") })
            {
                var tab = new UnderlineTabButton { Text = item.Item1, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Surface, ForeColor = Muted,
                    Selected = register == (item.Item2 == "register"), Font = new Font(Font.FontFamily, 11f, FontStyle.Bold) };
                tab.Click += (_, _) => Mode(item.Item2); tabs.Controls.Add(tab);
            }
            Row(tabs, 54);
        }
        Row(new Label { Text = title, ForeColor = Ink, Font = new Font(Font.FontFamily, 23, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 60);
        if (!signedIn) Row(new Label { Text = register ? "建立账户，管理你的游戏与配置" : reset ? "验证邮箱后设置新密码" : "使用邮箱和密码继续", ForeColor = Muted }, 32);
        if (signedIn && !change)
        {
            var account = accountClient.Account!;
            Row(new Label { Text = account.Email, Font = new Font(Font.FontFamily, 13), AutoEllipsis = true }, 48);
            Row(new Label { Text = account.Membership.Tier == "pro" ? "Pro 用户" : "普通用户", ForeColor = Acid, Font = new Font(Font.FontFamily, 21, FontStyle.Bold) }, 55);
            Row(new Label { Text = account.Membership.ExpiresAt is { } expiry ? "Pro 有效期至 " + expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : account.Membership.Tier == "pro" ? "Pro 长期有效" : "当前账户使用普通用户权限。", ForeColor = Muted }, 44);
            Row(new Label { Text = accountClient.IsOnline ? "已联网验证" : "连接中断：重新联网验证后可使用功能。", ForeColor = accountClient.IsOnline ? Acid : Muted }, 44);
            ButtonRow(accountClient.IsOnline ? "进入游戏库 →" : "重新连接账户服务", async (_, _) => await RunAccountActionAsync(async () =>
            {
                await accountClient.HeartbeatAsync(lifetime.Token); accountFeedback = "账户状态已更新。";
                if (await RequireFeatureAsync("library.manage")) { SwitchPage(0); if (libraryGames.Count == 0) await ScanGamesAsync(); }
            }, "正在验证账户…"), true);
            ButtonRow("刷新会员状态", async (_, _) => await RunAccountActionAsync(async () => { await accountClient.HeartbeatAsync(lifetime.Token); accountFeedback = "会员状态已更新。"; }, "正在刷新会员状态…"));
            Row(new Label { Text = "Pro 购买暂未开放。开通方式与可用权益将以正式上线内容为准。", ForeColor = Muted }, 64);
            ButtonRow("修改密码", (_, _) => Mode("password"));
            ButtonRow("退出登录", async (_, _) =>
            {
                if (busy || accountTransactionDepth > 0) { feedback.Text = "请等待当前操作安全完成后再退出登录。"; return; }
                await RunAccountActionAsync(async () => { await accountClient.LogoutAsync(lifetime.Token); accountMode = "login"; accountFeedback = "已退出登录。"; SwitchPage(6); }, "正在退出登录…");
            });
        }
        else
        {
            TextBox? email = change ? null : Field("邮箱地址");
            TextBox? code = null;
            if (register || reset)
            {
                code = Field("邮箱验证码", max: 6);
                var codeButton = Action("获取验证码", "Send code", async (_, _) =>
                {
                    if (!ValidateEmail(email!)) return;
                    var address = email!.Text.Trim(); var purpose = register ? "register" : "reset";
                    var key = purpose + ":" + address;
                    if (codeCooldowns.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow)
                    { feedback.Text = $"请在 {(int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds)} 秒后重新获取验证码。"; return; }
                    if (accountRequestBusy) return;
                    accountRequestBusy = true; form.Enabled = false;
                    try
                    {
                        var result = await accountClient.SendCodeAsync(address, purpose, lifetime.Token);
                        codeCooldowns[key] = DateTimeOffset.UtcNow.AddSeconds(result.RetryAfterSeconds); feedback.Text = result.Message;
                    }
                    catch (Exception e)
                    {
                        if (e is AccountApiException { RetryAfterSeconds: > 0 } api) codeCooldowns[key] = DateTimeOffset.UtcNow.AddSeconds(api.RetryAfterSeconds.Value);
                        feedback.Text = AccountError(e);
                    }
                    finally { accountRequestBusy = false; if (!form.IsDisposed) form.Enabled = true; }
                });
                codeButton.Dock = DockStyle.Right; codeButton.Width = S(110); codeButton.Radius = 8;
                codeButton.BackColor = SelectedSurface; codeButton.ForeColor = Acid;
                codeButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                code.Parent!.Controls.Add(codeButton);
            }
            var oldPassword = change ? Field("当前密码", true, 128) : null;
            var password = Field(register || reset || change ? "新密码（至少 8 位）" : "密码", true, 128);
            var confirmation = register || reset || change ? Field("确认密码", true, 128) : null;
            CheckBox? terms = null;
            if (register) { terms = new CheckBox { Text = "我已阅读并同意用户协议和隐私政策", ForeColor = Muted }; Row(terms, 40); }
            if (!reset && !change)
            {
                var keep = new CheckBox { Text = "保持登录状态（此 Windows 用户）", Checked = keepAccountLogin, ForeColor = Muted };
                keep.CheckedChanged += (_, _) => keepAccountLogin = keep.Checked; Row(keep, 38);
            }
            ButtonRow(change || reset ? "保存新密码 →" : register ? "创建账户 →" : "登录 →", async (_, _) =>
            {
                if (email != null && !ValidateEmail(email)) return;
                if (!ValidatePassword(password, confirmation)) return;
                if (code != null && (code.Text.Length != 6 || !code.Text.All(char.IsAsciiDigit))) { feedback.Text = "请输入 6 位数字验证码。"; return; }
                if (terms != null && !terms.Checked) { feedback.Text = "请先阅读并同意用户协议和隐私政策。"; return; }
                if (oldPassword != null && oldPassword.Text.Length == 0) { feedback.Text = "请输入当前密码。"; return; }
                var address = email?.Text.Trim() ?? ""; var secret = password.Text; var verification = code?.Text ?? ""; var current = oldPassword?.Text ?? "";
                bool loggedIn = false;
                await RunAccountActionAsync(async () =>
                {
                    if (change) { await accountClient.ChangePasswordAsync(current, secret, lifetime.Token); accountMode = "login"; accountFeedback = "密码已修改，请使用新密码重新登录。"; SwitchPage(6); }
                    else if (reset) { await accountClient.ResetPasswordAsync(address, verification, secret, lifetime.Token); accountMode = "login"; accountFeedback = "密码已重置，请使用新密码登录。"; }
                    else
                    {
                        if (register) await accountClient.RegisterAsync(address, verification, secret, keepAccountLogin, lifetime.Token);
                        else await accountClient.LoginAsync(address, secret, keepAccountLogin, lifetime.Token);
                        accountMode = "login"; accountFeedback = accountClient.StorageWarning ?? "登录成功。";
                        loggedIn = true;
                    }
                }, "正在连接账户服务…");
                if (loggedIn && accountClient.IsOnline && !IsDisposed) { SwitchPage(0); await ScanGamesAsync(); }
            }, true);
            if (!signedIn && !register && !reset) LinkRow("忘记密码？", (_, _) => Mode("reset"));
            LinkRow(signedIn ? "返回账户" : register || reset ? "已有账户？返回登录" : "没有账户？立即注册", (_, _) => Mode(!signedIn && !register && !reset ? "register" : "login"));
        }
        var legal = new FlowLayoutPanel { WrapContents = false, BackColor = Color.Transparent, Padding = Padding.Empty };
        foreach (var link in new[] { ("用户协议", "terms"), ("隐私政策", "privacy") })
        {
            var button = new LinkLabel { Text = link.Item1, LinkColor = Muted, ActiveLinkColor = Acid, VisitedLinkColor = Muted,
                LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent, AutoSize = true, Margin = new Padding(0, S(4), S(20), 0) };
            button.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(AccountApiClient.DefaultBaseUrl + "legal/" + link.Item2) { UseShellExecute = true }); }
                catch (Exception e) { feedback.Text = e.Message; }
            }; legal.Controls.Add(button);
        }
        Row(legal, 32); Row(feedback, 48);
        panel.Controls.Add(form); background.Controls.Add(panel); accountPage.Controls.Add(background);
        void LayoutPage()
        {
            formHeight = 0;
            for (var i = 0; i < rowHeights.Count; i++)
            {
                form.RowStyles[i].Height = S(rowHeights[i]); formHeight += S(rowHeights[i]);
                if (form.GetControlFromPosition(0, i) is { } child) child.Margin = new Padding(0, 0, 0, S(10));
            }
            background.SetBounds(0, 0, Math.Max(S(960), accountPage.ClientSize.Width), Math.Max(accountPage.ClientSize.Height, formHeight + S(100)));
            var panelWidth = Math.Clamp((int)(background.Width * .40), S(410), S(560));
            var panelHeight = formHeight + S(56);
            var panelX = background.Width - panelWidth - S(32);
            panel.SetBounds(panelX, Math.Max(S(24), (background.Height - panelHeight) / 2), panelWidth, panelHeight);
            headline.SetBounds(S(52), S(105), panelX - S(100), S(155));
            description.SetBounds(S(54), S(285), panelX - S(104), S(260));
            form.SetBounds(S(28), S(28), panelWidth - S(56), formHeight);
            accountPage.AutoScrollMinSize = new Size(0, background.Height);
        }
        EventHandler resize = (_, _) => LayoutPage(); accountPage.ClientSizeChanged += resize;
        background.Disposed += (_, _) => accountPage.ClientSizeChanged -= resize;
        LayoutPage(); accountPage.AutoScrollPosition = Point.Empty;
    }
}
