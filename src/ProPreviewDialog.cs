using System.Drawing;
using System.Windows.Forms;

namespace AmdNrAssistant;

// Isolated purchase-flow preview. It never calls a payment endpoint or changes membership.
internal sealed class ProPreviewDialog : Form
{
    static readonly Color Canvas = SmartRecommendationDialog.Canvas;
    static readonly Color Header = SmartRecommendationDialog.Header;
    static readonly Color Surface = SmartRecommendationDialog.Surface;
    static readonly Color Rule = SmartRecommendationDialog.Rule;
    static readonly Color Ink = SmartRecommendationDialog.Ink;
    static readonly Color Muted = SmartRecommendationDialog.Muted;
    static readonly Color Accent = SmartRecommendationDialog.Accent;

    readonly Panel header = new(), body = new(), footer = new();
    readonly SmartPrototypeLabel logo = new(), title = new(), badge = new(), intro = new();
    readonly SmartPrototypeLabel plansHeading = new(), featuresHeading = new(), stateTitle = new(), stateDetail = new(), footerNote = new();
    readonly SmartPrototypeButton close = new(), monthly = new(), yearly = new(), next = new(), reset = new();
    readonly RoundedPanel stateCard = new() { Radius = 16 };
    readonly RoundedPanel[] featureCards = new RoundedPanel[3];
    readonly SmartPrototypeLabel[] featureTitles = new SmartPrototypeLabel[3];
    readonly SmartPrototypeLabel[] featureDetails = new SmartPrototypeLabel[3];
    int selectedPlan = -1;
    int stage;

    int P(int value) => (int)Math.Round(value * DeviceDpi / 96d);
    void Place(Control control, int x, int y, int width, int height) =>
        control.SetBounds(P(x), P(y), P(width), P(height));

    SmartPrototypeLabel Label(Control parent, string value, int pixels, Color color, bool bold = false)
    {
        var label = new SmartPrototypeLabel
        {
            Text = value, BackColor = Color.Transparent, ForeColor = color,
            Font = new Font("Microsoft YaHei", P(pixels), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
            AutoEllipsis = true
        };
        parent.Controls.Add(label);
        return label;
    }

    public ProPreviewDialog()
    {
        Text = "MU Pro · 购买流程预览";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        BackColor = Canvas;
        ClientSize = new Size(P(940), P(670));
        MinimumSize = Size;
        MaximumSize = Size;

        header.BackColor = Header;
        body.BackColor = Canvas;
        footer.BackColor = Header;
        Controls.AddRange([header, body, footer]);

        logo = Label(header, "MU", 19, Color.FromArgb(8, 16, 7), true);
        logo.BackColor = Accent;
        logo.TextAlign = ContentAlignment.MiddleCenter;
        title = Label(header, "Pro · 购买流程", 22, Ink, true);
        badge = Label(header, "测试预览 · 不收款", 12, Accent, true);
        close.PaintStyle = SmartPrototypeButton.Style.Close;
        close.AccessibleName = "关闭预览";
        close.Click += (_, _) => Close();
        header.Controls.Add(close);

        intro = Label(body, "选择方案，看看购买与开通会怎样进行。", 14, Muted);
        plansHeading = Label(body, "01  选择套餐", 17, Ink, true);
        monthly.PaintStyle = yearly.PaintStyle = SmartPrototypeButton.Style.Goal;
        monthly.Text = "月度 Pro";
        monthly.Detail = "30 天 · 价格待定";
        yearly.Text = "年度 Pro";
        yearly.Detail = "365 天 · 价格待定";
        body.Controls.AddRange([monthly, yearly]);
        monthly.Click += (_, _) => SelectPlan(0);
        yearly.Click += (_, _) => SelectPlan(1);

        featuresHeading = Label(body, "计划提供", 17, Ink, true);
        string[] names = ["批量游戏管理", "配置历史", "性能对比"];
        string[] details = ["统一检查多款游戏", "保存并恢复配置版本", "比较不同方案的实测数据"];
        for (var i = 0; i < featureCards.Length; i++)
        {
            featureCards[i] = new RoundedPanel { BackColor = Surface, BorderColor = Rule, Radius = 13 };
            body.Controls.Add(featureCards[i]);
            featureTitles[i] = Label(featureCards[i], names[i], 14, Ink, true);
            featureDetails[i] = Label(featureCards[i], details[i], 11, Muted);
        }

        stateCard.BackColor = Surface;
        stateCard.BorderColor = Rule;
        body.Controls.Add(stateCard);
        stateTitle = Label(stateCard, "02  等待选择套餐", 17, Ink, true);
        stateDetail = Label(stateCard, "支付平台尚未接入。这里展示下单与开通的交互，不会生成真实订单。", 12, Muted);
        footerNote = Label(footer, "演示不会扣款，也不会改变账户会员状态。", 11, Muted);
        reset.PaintStyle = next.PaintStyle = SmartPrototypeButton.Style.Action;
        reset.Text = "重新体验";
        next.Primary = true;
        next.Click += (_, _) => Advance();
        reset.Click += (_, _) => { selectedPlan = -1; stage = 0; Render(); };
        footer.Controls.AddRange([reset, next]);

        Shown += (_, _) => LayoutContent();
        Resize += (_, _) => LayoutContent();
        Render();
        LayoutContent();
    }

    void SelectPlan(int plan)
    {
        selectedPlan = plan;
        stage = 0;
        Render();
    }

    void Advance()
    {
        if (selectedPlan < 0 || stage >= 2) return;
        stage++;
        Render();
    }

    void Render()
    {
        monthly.IsSelected = selectedPlan == 0;
        yearly.IsSelected = selectedPlan == 1;
        monthly.Invalidate(); yearly.Invalidate();
        next.Enabled = selectedPlan >= 0 && stage < 2;
        next.Text = stage switch
        {
            0 => "模拟进入支付 →",
            1 => "模拟平台确认 →",
            _ => "演示完成"
        };
        reset.Visible = selectedPlan >= 0 || stage > 0;
        var name = selectedPlan == 0 ? "月度 Pro" : "年度 Pro";
        (stateTitle.Text, stateDetail.Text) = stage switch
        {
            1 => ("02  等待支付平台", $"已选择{name}。正式版将打开支付页面，并等待服务端核验订单。"),
            2 => ("03  演示已完成", "正式版只有在服务端确认付款后才会开通 Pro；本次没有创建订单或开通权益。"),
            _ when selectedPlan >= 0 => ("02  准备下单", $"已选择{name}。价格与支付入口将在平台审核通过后确定。"),
            _ => ("02  等待选择套餐", "支付平台尚未接入。这里展示下单与开通的交互，不会生成真实订单。")
        };
    }

    void LayoutContent()
    {
        if (IsDisposed) return;
        Place(header, 0, 0, 940, 78);
        Place(body, 0, 78, 940, 515);
        Place(footer, 0, 593, 940, 77);
        Place(logo, 26, 15, 48, 48);
        Place(title, 92, 15, 390, 48);
        Place(badge, 650, 15, 220, 48);
        Place(close, 876, 19, 38, 38);
        Place(intro, 30, 18, 870, 32);
        Place(plansHeading, 30, 61, 400, 32);
        Place(monthly, 30, 101, 430, 83);
        Place(yearly, 480, 101, 430, 83);
        Place(featuresHeading, 30, 200, 400, 32);
        for (var i = 0; i < featureCards.Length; i++)
        {
            Place(featureCards[i], 30 + i * 300, 241, 280, 85);
            Place(featureTitles[i], 17, 12, 246, 25);
            Place(featureDetails[i], 17, 42, 246, 24);
        }
        Place(stateCard, 30, 349, 880, 125);
        Place(stateTitle, 22, 18, 825, 33);
        Place(stateDetail, 22, 59, 825, 45);
        Place(footerNote, 30, 17, 550, 42);
        Place(reset, 615, 16, 130, 44);
        Place(next, 760, 16, 150, 44);
    }
}
