using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task ShowManualPurchaseDialogAsync()
    {
        var plans = await accountClient.GetPurchasePlansAsync(lifetime.Token);
        if (!plans.CdkPlanMatching || plans.Items.Length == 0)
            throw new InvalidOperationException("后台尚不支持套餐 CDK 校验，或暂无上架套餐，请联系 xrwCoder。");
        using var dialog = new Form { Text="MU · 套餐 CDK 兑换", BackColor=Surface, ForeColor=Ink,
            StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false, MinimizeBox=false, ShowInTaskbar=false, ClientSize=new Size(560,650), Font=Font };
        var layout = new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), FlowDirection=FlowDirection.TopDown,
            WrapContents=false, AutoScroll=true, BackColor=Surface };
        var selector = new ComboBox { Width=490, DropDownStyle=ComboBoxStyle.DropDownList };
        foreach (var plan in plans.Items) selector.Items.Add($"{plan.Name} · ¥{plan.PriceMinor/100m:0.00}");
        var guide = new Label { Width=490, Height=95, ForeColor=Ink,
            Text="支付成功后添加微信 xrwCoder，发送付款凭证和所选套餐，获取会员 CDK。\n付款不会自动开通；兑换码必须与所选套餐匹配。" };
        var qr = new PictureBox { Width=220, Height=220, SizeMode=PictureBoxSizeMode.Zoom };
        var image = plans.Payment?.Channels?.FirstOrDefault()?.QrImageUrl ?? plans.Payment?.QrImageUrl;
        if (image?.StartsWith("data:image/",StringComparison.Ordinal)==true && image.Contains("base64,"))
        {
            try { using var bytes=new MemoryStream(Convert.FromBase64String(image[(image.IndexOf("base64,",StringComparison.Ordinal)+7)..])); using var raw=Image.FromStream(bytes); qr.Image=new Bitmap(raw); }
            catch (Exception e) when(e is ArgumentException or FormatException) { guide.Text+="\n收款码暂不可用，请联系作者核对付款方式。"; }
        }
        qr.Visible = qr.Image != null;
        var input = new TextBox { Width=490, PlaceholderText="输入所选套餐对应的 CDK", MaxLength=80, Visible=false };
        var status = new Label { Width=490, Height=55, ForeColor=Muted };
        var action = new Button { Width=250, Height=44, Text="支付完成 校验CDK" };
        var copy = new Button { Width=200, Height=38, Text="复制微信 xrwCoder" };
        copy.Click+=(_,_)=>Clipboard.SetText("xrwCoder");
        var processing=false;
        action.Click+=async(_,_)=>
        {
            if(!input.Visible){input.Visible=true;selector.Enabled=false;action.Text="校验 CDK · 兑换权益";input.Focus();return;}
            if(string.IsNullOrWhiteSpace(input.Text)){status.Text="请输入会员 CDK。";return;}
            processing=true;action.Enabled=false;input.Enabled=false;status.Text="正在校验…";
            try
            {
                await accountClient.RedeemMembershipAsync(input.Text.Trim(),lifetime.Token,plans.Items[selector.SelectedIndex].Id);
                input.Clear();status.Text="兑换成功，会员及服务权益已更新。";action.Text="已兑换";
            }
            catch(Exception e){status.Text=AccountError(e);action.Enabled=true;input.Enabled=true;}
            finally{processing=false;}
        };
        dialog.FormClosing+=(_,e)=>{if(processing)e.Cancel=true;};
        dialog.FormClosed+=(_,_)=>qr.Image?.Dispose();
        layout.Controls.AddRange([selector,guide,qr,copy,input,action,status]);dialog.Controls.Add(layout);selector.SelectedIndex=0;
        dialog.ShowDialog(this);
    }
}
