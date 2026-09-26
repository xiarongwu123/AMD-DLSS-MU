using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    bool busy;
    async Task InspectSelectedAsync(bool runtime)
    {
        if (selectedGame == null || busy) return;
        var exe = selectedGame;
        if (!await RequireFeatureAsync("diagnostics.use") || busy) return;
        busy = true; libraryPage.Enabled = false;
        try
        {
            var check = await Task.Run(() => GameManagement.Check(exe, false));
            var record = GameManagement.Read(exe);
            var info = string.Join("\n", check.Details);
            if (record != null) info += $"\n\n{GameManagement.ModeName(record.Mode)} / {record.Version}\n安装时间：{record.InstalledUtc.ToLocalTime():g}\n状态：{record.Phase}\n备份：{GameManagement.State(exe)}";
            var result = runtime ? GameManagement.Runtime(exe) : check.Summary;
            status.Text = result;
            MessageBox.Show(this, result + "\n\n" + info, runtime ? "本次运行状态" : "兼容性与安装记录");
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { busy = false; libraryPage.Enabled = true; UpdateButtons(); }
    }
    async Task ExportDiagnosticAsync()
    {
        if (selectedGame == null || busy) return;
        await ExportDiagnosticForAsync(selectedGame);
    }
    async Task ExportDiagnosticForAsync(string exe)
    {
        if (busy) return;
        if (!await RequireFeatureAsync("diagnostics.use") || busy) return;
        busy = true; libraryPage.Enabled = false;
        try
        {
            var text = await Task.Run(() => Diagnostics.Export(exe));
            using var preview = new Form { Text = "诊断预览（仅保存本地，不自动上传）", Width = 760, Height = 580, StartPosition = FormStartPosition.CenterParent };
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = text };
            var save = new Button { Text = "保存诊断 JSON", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK };
            preview.Controls.Add(box); preview.Controls.Add(save);
            if (preview.ShowDialog(this) != DialogResult.OK) return;
            using var dialog = new SaveFileDialog { Filter = "诊断文件 (*.json)|*.json", FileName = "AMD-DLSS-MU-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
            if (dialog.ShowDialog(this) == DialogResult.OK && await RequireFeatureAsync("diagnostics.use")) { await File.WriteAllTextAsync(dialog.FileName, text); status.Text = "诊断已保存，包含脱敏日志与配置；分享前请再次检查个人信息。"; }
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { busy = false; libraryPage.Enabled = true; UpdateButtons(); }
    }
}
