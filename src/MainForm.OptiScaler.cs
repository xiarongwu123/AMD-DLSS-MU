using System.Windows.Forms;
namespace AmdNrAssistant;
public sealed partial class MainForm
{
    async Task InstallOptiScalerStandardAsync(string exe)
    {
        using var setup = new Form { Text = "OptiScaler " + OptiInstaller.Version, Width = 560, Height = 260, StartPosition = FormStartPosition.CenterParent };
        var info = new Label { Text = "自动下载并验证官方标准版；安装超分/帧生成适配，不安装 DLSS 5 神经渲染。\n\n选择游戏实际使用的图形接口，安装后按 Insert 调节。\n部分游戏仍需专用兼容设置，不自动添加 OptiPatcher。", Dock = DockStyle.Top, Height = 115 };
        var api = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList }; api.Items.AddRange(new[] { "DirectX 11 / 12（dxgi.dll）", "Vulkan（winmm.dll）" }); api.SelectedIndex = 0;
        var go = new Button { Text = "下载并安装", Dock = DockStyle.Bottom, Height = 44, DialogResult = DialogResult.OK };
        setup.Controls.Add(api); setup.Controls.Add(info); setup.Controls.Add(go);
        if (setup.ShowDialog(this) != DialogResult.OK) { Diagnostics.Record(exe, "optiscaler-install", "cancelled"); return; }
        bool vulkan = api.SelectedIndex == 1;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var download = new Form { Text = "OptiScaler 下载与安装", Width = 520, Height = 190, StartPosition = FormStartPosition.CenterParent, ControlBox = false };
        var label = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "准备下载…" };
        var stop = new Button { Dock = DockStyle.Bottom, Height = 40, Text = "取消下载" }; stop.Click += (_, _) => { cancel.Cancel(); stop.Enabled = false; label.Text = "正在取消…"; };
        download.Controls.Add(label); download.Controls.Add(stop); download.Show(this);
        try
        {
            var progress = new Progress<string>(s => { if (!download.IsDisposed) label.Text = s; status.Text = s; });
            var package = await OptiInstaller.PrepareAsync(exe, progress, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested(); stop.Enabled = false; label.Text = "正在写入组件并建立恢复记录…";
            await Task.Run(() => OptiInstaller.Install(exe, package, vulkan));
            status.Text = "OptiScaler 文件已安装；游戏内按 Insert。运行效果尚未验证。";
            RefreshHome();
        }
        finally { download.Close(); }
    }
}
