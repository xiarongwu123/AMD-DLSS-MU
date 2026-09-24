using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    bool checkingUpdate;
    async Task CheckAppUpdateAsync(bool automatic)
    {
        if (checkingUpdate || busy) return;
        checkingUpdate = true;
        bool ownsBusy = false;
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
            ownsBusy = true;
            UpdateButtons();
            automatic = false;
            using var session = BeginDownloadSession("启动器更新 " + release.Tag, () => CheckAppUpdateAsync(false));
            SwitchPage(3);
            var candidate = await AutoUpdate.DownloadAsync(release,
                new Progress<int>(n => status.Text = n == 100 ? "下载完成，正在校验…" : $"正在下载更新 {n}%"),
                lifetime.Token, message => status.Text = message);
            if (MessageBox.Show(this, "新版已下载并校验。现在退出并重启更新？", "准备更新", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Enabled = false;
            var helper = await Task.Run(() => AutoUpdate.Prepare(candidate, release));
            AutoUpdate.StartHelper(helper);
            busy = false;
            Application.Exit();
        }
        catch (OperationCanceledException) { status.Text = "已取消更新，当前版本保持不变。"; }
        catch (Exception e)
        {
            if (!IsDisposed) { status.Text = "更新检查/下载失败，可稍后重试。"; if (!automatic) MessageBox.Show(this, e.Message, "更新未完成"); }
        }
        finally { checkingUpdate = false; if (ownsBusy) busy = false; if (!IsDisposed) { Enabled = true; UpdateButtons(); } }
    }
}
