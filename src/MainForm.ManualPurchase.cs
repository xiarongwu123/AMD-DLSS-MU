using System.Diagnostics;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task ShowManualPurchaseDialogAsync()
    {
        var plans = await accountClient.GetPurchasePlansAsync(lifetime.Token);
        if (!plans.PaymentsEnabled || plans.PaymentMethod != "manual" || plans.Items.Length == 0)
            throw new InvalidOperationException("人工购买申请尚未开放，或暂无上架套餐。");
        var orders = await accountClient.GetPurchaseOrdersAsync(lifetime.Token);

        using var dialog = new Form
        {
            Text = "MU Pro · 购买申请", BackColor = Surface, ForeColor = Ink,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            ClientSize = new Size(560, 600), Font = new Font(Font.FontFamily, 10f)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 20), ColumnCount = 1,
            RowCount = 10, BackColor = Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 48, 48, 26, 42, 40, 108, 23, 40, 50, 56 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Label Label(string value, Color color, float size = 10, bool bold = false) => new()
        {
            Text = value, ForeColor = color, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoEllipsis = false
        };
        var title = Label("申请 MU Pro", Ink, 21, true);
        var note = Label("选套餐 → 人工付款 → 管理员核对到账并开通。请保留付款凭证。", Muted);
        var planCaption = Label("会员套餐", Muted, 9);
        var selector = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Base,
            ForeColor = Ink, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, 11)
        };
        foreach (var plan in plans.Items)
            selector.Items.Add($"{plan.Name} · {plan.DurationDays} 天 · ¥{plan.PriceMinor / 100m:0.00}");
        selector.SelectedIndex = 0;
        var amount = Label("", Ink, 12, true);
        void UpdateAmount()
        {
            var plan = plans.Items[selector.SelectedIndex];
            amount.Text = $"应付 ¥{plan.PriceMinor / 100m:0.00}  ·  {plan.DurationDays} 天";
        }
        selector.SelectedIndexChanged += (_, _) => UpdateAmount();
        UpdateAmount();
        var instructions = Label("提交申请后显示收款说明。", Muted);
        var orderCaption = Label("订单编号", Muted, 9);
        var orderId = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = Base, ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10)
        };
        var qrLink = new LinkLabel
        {
            Text = "", Dock = DockStyle.Fill, LinkColor = Acid, ActiveLinkColor = Acid,
            VisitedLinkColor = Acid, TextAlign = ContentAlignment.MiddleLeft
        };
        var state = Label("付款不会自动开通；只有核对到账后，会员状态才会更新。", Muted, 9);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Surface };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        RoundedButton Button(string text, bool primary = false) => new()
        {
            Text = text, Dock = DockStyle.Fill, Margin = new Padding(4), Radius = 12,
            BackColor = primary ? Acid : Sidebar, ForeColor = primary ? OnAccent : Ink
        };
        var copy = Button("复制订单号");
        var refresh = Button("刷新状态");
        var create = Button("提交申请", true);
        copy.Enabled = refresh.Enabled = false;
        actions.Controls.Add(copy, 0, 0); actions.Controls.Add(refresh, 1, 0); actions.Controls.Add(create, 2, 0);
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(note, 0, 1);
        layout.Controls.Add(planCaption, 0, 2); layout.Controls.Add(selector, 0, 3);
        layout.Controls.Add(amount, 0, 4); layout.Controls.Add(instructions, 0, 5);
        layout.Controls.Add(orderCaption, 0, 6); layout.Controls.Add(orderId, 0, 7);
        layout.Controls.Add(qrLink, 0, 8); layout.Controls.Add(state, 0, 9);
        dialog.Controls.Add(layout);
        // Keep actions in a separate bottom bar so narrow DPI scaling cannot hide them.
        actions.Dock = DockStyle.Bottom;
        actions.Height = 58;
        dialog.Controls.Add(actions);
        actions.BringToFront();

        PurchaseOrder? current = null;
        void ShowOrder(PurchaseOrder order)
        {
            current = order;
            var planIndex = Array.FindIndex(plans.Items, x => x.Id == order.PlanId);
            if (planIndex >= 0) selector.SelectedIndex = planIndex;
            amount.Text = $"本单应付 ¥{order.AmountMinor / 100m:0.00}  ·  {order.DurationDays} 天";
            orderId.Text = order.Id;
            selector.Enabled = order.Status is not ("pending" or "review_requested");
            create.Enabled = order.Status != "review_requested";
            create.Text = order.Status switch
            {
                "pending" => "我已付款，提交核对",
                "review_requested" => "等待核对",
                "paid" => "再次购买",
                _ => "提交申请"
            };
            copy.Enabled = true;
            refresh.Enabled = true;
            var payment = order.Payment;
            instructions.Text = order.Status is "pending" or "review_requested"
                ? $"待核对 · 应付 ¥{order.AmountMinor / 100m:0.00}\n{payment?.Instructions ?? "收款信息暂不可用，请联系管理员。"}"
                : order.Status == "paid" ? "已确认到账，Pro 已开通。" : "订单已结束。";
            state.Text = order.Status == "pending" ? "付款后点击“我已付款”；管理员核对到账后再刷新。"
                : order.Status == "review_requested" ? "已提交核对；尚未开通，请保留付款凭证。"
                : order.Status == "paid" ? "已开通，可关闭窗口并刷新账户状态。" : "可重新选择套餐提交申请。";
            qrLink.Text = (order.Status is "pending" or "review_requested") && Uri.TryCreate(payment?.QrImageUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps ? "打开收款二维码 ↗" : "";
        }
        qrLink.LinkClicked += (_, _) =>
        {
            if (Uri.TryCreate(current?.Payment?.QrImageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception e) { state.Text = e.Message; }
        };
        copy.Click += (_, _) => { if (current != null) Clipboard.SetText(current.Id); };
        refresh.Click += async (_, _) =>
        {
            if (current == null) return;
            refresh.Enabled = false;
            try { ShowOrder(await accountClient.GetPurchaseOrderAsync(current.Id, lifetime.Token)); }
            catch (Exception e) { state.Text = AccountError(e); }
            finally { refresh.Enabled = true; }
        };
        create.Click += async (_, _) =>
        {
            create.Enabled = false;
            state.Text = current?.Status == "pending" ? "正在提交核对…" : "正在提交申请…";
            try
            {
                if (current?.Status == "pending")
                {
                    await accountClient.SubmitPurchaseForReviewAsync(current.Id, lifetime.Token);
                    ShowOrder(await accountClient.GetPurchaseOrderAsync(current.Id, lifetime.Token));
                }
                else ShowOrder(await accountClient.CreatePurchaseOrderAsync(plans.Items[selector.SelectedIndex].Id, lifetime.Token));
            }
            catch (Exception e) { state.Text = AccountError(e); create.Enabled = true; }
        };
        var pending = orders.Items.FirstOrDefault(x => x.Status is "pending" or "review_requested");
        if (pending != null) ShowOrder(await accountClient.GetPurchaseOrderAsync(pending.Id, lifetime.Token));
        dialog.ShowDialog(this);
    }
}
