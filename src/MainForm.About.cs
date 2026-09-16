using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    static Image LoadWeChatQr()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AmdNrAssistant.WeChatQr")
            ?? throw new IOException("微信二维码资源缺失。");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    void BuildAbout()
    {
        aboutPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(28) };
        var stack = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        stack.Controls.Add(Heading("关于 AMD DLSS MU", "About AMD DLSS MU", 22));
        stack.Controls.Add(new Label { AutoSize = true, ForeColor = muted, Text = "v" + AutoUpdate.DisplayVersion + "  ·  codeXia", Margin = new Padding(0, 0, 0, 12) });
        var paragraphs = new List<Label>();
        void Paragraph(string zh, string en)
        {
            var label = Localize(new Label { AutoSize = true, ForeColor = ink, Margin = new Padding(0, 0, 0, 12) }, zh, en);
            paragraphs.Add(label); stack.Controls.Add(label);
        }
        Paragraph("免费游戏图形组件配置助手。集中管理组件安装、配置恢复、运行状态与诊断，让尝试和排查更方便。",
            "A free helper for game graphics components: installation, restoration, runtime status and diagnostics in one place.");
        Paragraph("模式一：AMD 神经渲染运行时方案。\n模式二：OptiScaler 标准版，提供兼容游戏的超分 / 帧生成配置；不等于 DLSS 5 神经渲染。",
            "Mode 1: AMD neural-rendering runtime setup.\nMode 2: standard OptiScaler for upscaling / frame generation in compatible games; this is not DLSS 5 neural rendering.");
        stack.Controls.Add(Heading("一起测试，一起改进", "Test and improve together", 16));
        Paragraph("遇到问题、分享实测效果，或想交流游戏设置？欢迎加入讨论组。\n用微信扫描下方二维码添加作者，备注「AMD DLSS MU」，添加后邀请你入群。",
            "Share results, troubleshoot issues and discuss game settings.\nScan the QR code in WeChat to add the developer. Mention “AMD DLSS MU” and ask for an invitation to the discussion group.");
        var qr = new PictureBox
        {
            Size = new Size(330, 294), SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadWeChatQr(), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 8),
            AccessibleName = "微信二维码 xrwCoder"
        };
        qr.Click += (_, _) => ShowWeChatQr();
        qr.Disposed += (_, _) => qr.Image?.Dispose();
        stack.Controls.Add(qr);
        Paragraph("微信号：xrwCoder（这是个人微信二维码，不是直接入群码）",
            "WeChat ID: xrwCoder (personal contact QR, not a direct group invitation)");
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 16) };
        actions.Controls.Add(Action("复制微信号", "Copy WeChat ID", (_, _) =>
        {
            try
            {
                Clipboard.SetText("xrwCoder");
                MessageBox.Show(this, english ? "Copied: xrwCoder" : "已复制微信号：xrwCoder", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }));
        actions.Controls.Add(Action("查看二维码大图", "Enlarge QR", (_, _) => ShowWeChatQr()));
        stack.Controls.Add(actions);
        stack.Controls.Add(Heading("反馈前，请准备这些信息", "Before reporting an issue", 14));
        Paragraph("游戏名称与版本、显卡型号、驱动版本、所选模式、分辨率、配置前后帧率，以及报错截图。可在游戏页面导出诊断，发送前请检查是否包含隐私信息。",
            "Include the game and version, GPU, driver, selected mode, resolution, FPS before/after, and error screenshots. Export diagnostics from the Games page and check for private information before sharing.");
        Paragraph("兼容性与性能因游戏、显卡和驱动而异，不保证所有游戏可用。切换方案前请关闭游戏并恢复配置；带反作弊的联网游戏请遵守游戏规则。\n本工具为独立第三方项目，非 AMD / NVIDIA 官方产品；相关商标和组件归各自权利人所有。第三方许可可在顶部查看。",
            "Compatibility and performance vary by game, GPU and driver. Close the game and restore its configuration before switching setups. Follow game rules, especially for online games with anti-cheat.\nAn independent third-party project, not an official AMD / NVIDIA product. Trademarks and components belong to their respective owners. See Licenses above.");
        aboutPanel.Controls.Add(stack);
        void Fit()
        {
            int width = Math.Max(300, aboutPanel.ClientSize.Width - aboutPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
            stack.MaximumSize = new Size(width, 0);
            foreach (var label in paragraphs) label.MaximumSize = new Size(width, 0);
            actions.MaximumSize = new Size(width, 0);
        }
        aboutPanel.ClientSizeChanged += (_, _) => Fit();
        pageHost.Controls.Add(aboutPanel); Fit();
    }

    void ShowWeChatQr()
    {
        using var dialog = new Form
        {
            Text = english ? "WeChat: xrwCoder · Ask to join the group" : "微信：xrwCoder · 添加后邀请入群",
            StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 710),
            MinimumSize = new Size(380, 360), MaximizeBox = true, MinimizeBox = false
        };
        using var image = LoadWeChatQr();
        dialog.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image });
        dialog.ShowDialog(this);
    }
}
