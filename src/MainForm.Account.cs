using System.Drawing.Drawing2D;
using System.Net.Mail;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel accountPage = new HudPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Base };
    bool registerAccount;

    void BuildAccountPage()
    {
        pageHost.Controls.Add(accountPage);
        RenderAccount();
    }

    void RenderAccount()
    {
        while (accountPage.Controls.Count > 0) { var c = accountPage.Controls[0]; accountPage.Controls.Remove(c); c.Dispose(); }
        translations.RemoveAll(item => item.control.IsDisposed);
        // No credentials are stored or transmitted until a real authentication provider is supplied.
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(48, 32, 48, 32), BackColor = Base };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        var hero = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 48, 24, 0) };
        var eyebrow = new Label { Text = "MU / ACCOUNT CENTER", AutoSize = true, ForeColor = Acid, Font = new Font("Consolas", 11), Margin = new Padding(0, 0, 0, 24) };
        hero.Controls.Add(eyebrow);
        hero.Controls.Add(new Label { Text = registerAccount ? "创建账户\n同步游戏体验" : "登录，继续\n释放每一帧", AutoSize = true, ForeColor = Ink, Font = new Font(Font.FontFamily, 32, FontStyle.Bold), Margin = new Padding(0, 0, 0, 18) });
        hero.Controls.Add(new Label { Text = registerAccount ? "MU 通行证" : "图形潜能", AutoSize = true, ForeColor = Acid, Font = new Font(Font.FontFamily, 32, FontStyle.Bold), Margin = new Padding(0, 0, 0, 28) });
        hero.Controls.Add(new Label { Text = "把时间留给游戏。\n本地游戏管理无需登录，即刻开始。", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 32) });
        var local = Action("进入本地游戏库 →", "Open local library →", (_, _) => SwitchPage(0)); local.Width = 240; local.Chamfer = true; hero.Controls.Add(local);
        content.Controls.Add(hero, 0, 0);
        var panel = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Surface, Radius = 2, BorderColor = Line, Padding = new Padding(28), Margin = new Padding(16, 0, 0, 0) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control c, int height)
        {
            var index = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            c.Dock = DockStyle.Fill; form.Controls.Add(c, 0, index);
        }
        var tabs = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var login = new UnderlineTabButton { Text = "登录", Selected = !registerAccount, Dock = DockStyle.Fill };
        var register = new UnderlineTabButton { Text = "注册", Selected = registerAccount, Dock = DockStyle.Fill };
        login.Click += (_, _) => { registerAccount = false; RenderAccount(); }; register.Click += (_, _) => { registerAccount = true; RenderAccount(); };
        tabs.Controls.Add(login, 0, 0); tabs.Controls.Add(register, 1, 0); Row(tabs, 52);
        Row(new Label { Text = registerAccount ? "创建 MU 账户" : "账号登录", Font = new Font(Font.FontFamily, 22, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft }, 70);
        Row(new Label { Text = registerAccount ? "建立账户后继续管理游戏" : "使用邮箱和密码继续", ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 42);
        TextBox Field(string label, string placeholder, bool password = false)
        {
            Row(new Label { Text = label, ForeColor = Muted, TextAlign = ContentAlignment.BottomLeft }, 30);
            var border = new RoundedPanel { BackColor = Base, Radius = 2, BorderColor = Line, Padding = new Padding(12, 12, password ? 68 : 12, 8), Margin = new Padding(0, 6, 0, 0) };
            var input = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Base, ForeColor = Ink, PlaceholderText = placeholder, UseSystemPasswordChar = password, AccessibleName = label, MaxLength = password ? 128 : 254 };
            border.Controls.Add(input);
            if (password)
            {
                var show = new Button { Text = "显示", Dock = DockStyle.Right, Width = 54, FlatStyle = FlatStyle.Flat, BackColor = Base, ForeColor = Acid, AccessibleName = "显示或隐藏" + label };
                show.FlatAppearance.BorderSize = 0; show.Click += (_, _) => { input.UseSystemPasswordChar = !input.UseSystemPasswordChar; show.Text = input.UseSystemPasswordChar ? "显示" : "隐藏"; };
                border.Padding = new Padding(12, 8, 4, 8); border.Controls.Add(show);
            }
            Row(border, 56); return input;
        }
        var feedback = new Label { Text = "账户服务尚未接入，可继续使用本地游戏库。", ForeColor = Muted, AutoEllipsis = false };
        var email = Field("邮箱地址", "name@example.com");
        TextBox? code = null;
        if (registerAccount)
        {
            code = Field("邮箱验证码", "输入 6 位验证码"); code.MaxLength = 6;
            var sendCode = Action("获取验证码", "Send code", (_, _) => { feedback.Text = "验证码服务尚未接入，尚未发送邮件。"; feedback.ForeColor = Acid; });
            sendCode.ForeColor = Acid; Row(sendCode, 38);
        }
        var password = Field(registerAccount ? "设置密码" : "密码", "至少 8 位字符", true);
        var confirmation = registerAccount ? Field("确认密码", "再次输入密码", true) : null;
        var agreement = new CheckBox { Text = registerAccount ? "我已阅读并同意用户协议与隐私政策" : "保持登录状态", ForeColor = Muted, Enabled = !registerAccount };
        Row(agreement, 42);
        if (!registerAccount)
        {
            var forgot = Action("忘记密码？", "Forgot password?", (_, _) => { feedback.Text = "密码找回服务尚未接入，当前可继续使用本地游戏库。"; feedback.ForeColor = Acid; });
            forgot.ForeColor = Acid; Row(forgot, 36);
        }
        var submit = Action(registerAccount ? "创建账户 →" : "登录 →", registerAccount ? "Create account →" : "Sign in →", (_, _) =>
        {
            feedback.ForeColor = Acid;
            if (!MailAddress.TryCreate(email.Text.Trim(), out var address) || address.Address != email.Text.Trim()) { feedback.Text = "请输入有效的邮箱地址。"; email.Focus(); return; }
            if (password.Text.Length < 8) { feedback.Text = "密码至少需要 8 位字符。"; password.Focus(); return; }
            if (registerAccount && (code!.Text.Length != 6 || !code.Text.All(char.IsAsciiDigit))) { feedback.Text = "请输入 6 位数字验证码。"; code.Focus(); return; }
            if (confirmation != null && confirmation.Text != password.Text) { feedback.Text = "两次输入的密码不一致。"; confirmation.Focus(); return; }
            feedback.Text = "账户服务尚未接入，未提交或保存任何账户信息。";
        });
        submit.BackColor = Acid; submit.ForeColor = OnAccent; submit.Chamfer = true; Row(submit, 54); Row(feedback, 66);
        var switchMode = Action(registerAccount ? "已有账号？返回登录" : "没有账号？立即注册", "Switch account mode", (_, _) => { registerAccount = !registerAccount; RenderAccount(); });
        Row(switchMode, 42);
        panel.Controls.Add(form); content.Controls.Add(panel, 1, 0); accountPage.Controls.Add(content);
        accountPage.AutoScrollPosition = Point.Empty;
    }
}
