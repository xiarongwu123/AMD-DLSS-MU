using System.Windows.Forms;

namespace AmdNrAssistant;

// Resource preparation only: never calls Install or changes a game directory.
internal sealed class AuthorResourcesTestForm : Form
{
    readonly TextBox output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
    readonly Button start = new() { Text = "开始检测", AutoSize = true };
    readonly Button cancel = new() { Text = "取消", AutoSize = true, Enabled = false };
    CancellationTokenSource? operation;
    bool closeWhenFinished;

    public AuthorResourcesTestForm()
    {
        Text = "MU 作者调校版 · 资源下载检测（支持 N 卡）";
        Width = 900; Height = 580;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        bar.Controls.Add(start); bar.Controls.Add(cancel);
        Controls.Add(output); Controls.Add(bar);
        output.Text = "检测 Mochizuki 资源包、提取组件和模型；已有有效缓存会复用。\r\n不会部署游戏文件，也不验证游戏运行效果。\r\n";
        start.Click += async (_, _) => await RunAsync();
        cancel.Click += (_, _) => operation?.Cancel();
        FormClosing += (_, e) =>
        {
            if (operation == null) return;
            e.Cancel = true; closeWhenFinished = true; operation.Cancel();
        };
    }

    async Task RunAsync()
    {
        start.Enabled = false; cancel.Enabled = true;
        using var cts = new CancellationTokenSource(); operation = cts;
        string? package = null;
        var lines = new List<string>();
        void Report(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + " " + message;
            lines.Add(line); output.AppendText(line + "\r\n");
        }
        var progress = new Progress<string>(Report);
        try
        {
            Report("开始准备作者调校版 Mochizuki 所需资源。");
            package = await NeuralRenderInstaller.PrepareAsync(progress, cts.Token, "mochizuki");
            Report("资源包下载及文件校验通过，开始准备模型。");
            await NeuralRenderInstaller.PrepareModelAsync(package, progress, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            Report("检测通过：资源包、提取组件及模型准备成功。尚未部署或验证游戏效果。");
        }
        catch (OperationCanceledException) { Report("检测已取消，未修改游戏文件。"); }
        catch (Exception ex) { Report("检测失败：\r\n" + ex); }
        finally
        {
            if (package != null)
            {
                try { Directory.Delete(package, true); }
                catch (Exception ex) { Report("临时解压目录清理失败：" + ex.Message); }
            }
            try
            {
                Directory.CreateDirectory(NeuralRenderInstaller.Resources);
                var log = Path.Combine(NeuralRenderInstaller.Resources, "author-resource-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
                await File.WriteAllLinesAsync(log, lines);
                output.AppendText("检测日志：" + log + "\r\n");
            }
            catch (Exception ex) { output.AppendText("日志保存失败：" + ex.Message + "\r\n"); }
            operation = null; start.Enabled = true; cancel.Enabled = false;
            if (closeWhenFinished) Close();
        }
    }
}
