using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    bool checkingUpdate;
    async Task CheckAppUpdateAsync(bool automatic)
    {
        if (checkingUpdate || busy) return;
        checkingUpdate = true;
        try
        {
            var release = await AutoUpdate.CheckAsync(lifetime.Token);
            if (release == null)
            {
                if (!automatic) MessageBox.Show(this, "当前已是最新正式版 v" + AutoUpdate.DisplayVersion, "检查更新");
                return;
            }
            if (busy || IsDisposed) return;
            if (MessageBox.Show(this, $"发现新版 {release.Tag}（当前 v{AutoUpdate.DisplayVersion}）\n下载大小：{release.Size / 1024d / 1024:0.0} MB\n\n{release.Notes}\n\n现在下载？安装记录与配置会保留。", "启动器更新", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            busy = true;
            automatic = false;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            using var dialog = new Form { Text = "下载启动器更新", Width = 440, Height = 160, StartPosition = FormStartPosition.CenterParent, ControlBox = false };
            var label = new Label { Text = "正在下载…", Dock = DockStyle.Top, Height = 32 };
            var bar = new ProgressBar { Dock = DockStyle.Top, Height = 24 };
            var button = new Button { Text = "取消下载", Dock = DockStyle.Bottom, Height = 35 };
            button.Click += (_, _) => cancel.Cancel();
            dialog.Controls.Add(bar); dialog.Controls.Add(label); dialog.Controls.Add(button);
            string? candidate = null; Exception? error = null;
            dialog.Shown += async (_, _) =>
            {
                try { candidate = await AutoUpdate.DownloadAsync(release, new Progress<int>(n => { bar.Value = n; label.Text = n == 100 ? "下载完成，正在校验…" : $"正在下载 {n}%"; }), cancel.Token); }
                catch (Exception e) { error = e; }
                finally { dialog.Close(); }
            };
            dialog.ShowDialog(this);
            if (error is OperationCanceledException) { status.Text = "已取消更新，当前版本保持不变。"; return; }
            if (error != null) throw error;
            if (candidate == null) return;
            if (MessageBox.Show(this, "新版已下载并校验。现在退出并重启更新？", "准备更新", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Enabled = false;
            var helper = await Task.Run(() => AutoUpdate.Prepare(candidate, release));
            AutoUpdate.StartHelper(helper);
            Application.Exit();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!IsDisposed) { status.Text = "更新检查/下载失败，可稍后重试。"; if (!automatic) MessageBox.Show(this, e.Message, "更新未完成"); }
        }
        finally { checkingUpdate = false; busy = false; if (!IsDisposed) { Enabled = true; UpdateButtons(); } }
    }
}
