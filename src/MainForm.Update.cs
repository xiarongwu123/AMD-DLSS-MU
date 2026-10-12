using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    bool checkingUpdate;
    async Task CheckAppUpdateAsync(bool automatic)
    {
        if (checkingUpdate || busy) return;
        if (!await RequireFeatureAsync("app.update") || checkingUpdate || busy) return;
        checkingUpdate = true;
        bool ownsBusy = false;
        CancellationToken downloadCancellation = default;
        try
        {
            var release = await AutoUpdate.CheckAsync(lifetime.Token);
            if (release == null)
            {
                if (!automatic) await UiMessageAsync( "官网当前没有比本机 v" + AutoUpdate.DisplayVersion + " 更新的正式版。", "检查更新");
                return;
            }
            if (busy || IsDisposed) return;
            var notes = string.IsNullOrWhiteSpace(release.Notes) ? "" : "\n\n" + release.Notes;
            if (await UiMessageAsync( $"官网发现新版 {release.Tag}（当前 v{AutoUpdate.DisplayVersion}）\n下载大小：{release.Size / 1024d / 1024:0.0} MiB{notes}\n\n现在从官网下载？安装记录与配置会保留。", "启动器更新", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (!await RequireFeatureAsync("app.update") || busy) return;
            busy = true;
            ownsBusy = true;
            UpdateButtons();
            automatic = false;
            using var session = BeginDownloadSession("启动器更新 " + release.Tag, () => CheckAppUpdateAsync(false), "app.update");
            downloadCancellation = session.Token;
            SwitchPage(3);
            var candidate = await AutoUpdate.DownloadAsync(release,
                new Progress<int>(n => status.Text = n == 100 ? "下载完成，正在校验…" : $"正在下载更新 {n}%"),
                lifetime.Token, message => status.Text = message);
            if (await UiMessageAsync( "新版已下载并校验。现在退出并重启更新？", "准备更新", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (!await RequireFeatureAsync("app.update")) return;
            using var transaction = BeginAccountTransaction();
            Enabled = false;
            var helper = await Task.Run(() => AutoUpdate.Prepare(candidate, release));
            AutoUpdate.StartHelper(helper);
            busy = false;
            Application.Exit();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || downloadCancellation.IsCancellationRequested)
        { if (ownsBusy && !IsDisposed) status.Text = "已取消更新，当前版本保持不变。"; }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !automatic)
            {
                status.Text = "更新请求超时，当前版本保持不变。";
                await UiMessageAsync("官网更新请求超时，请稍后重试。", "更新未完成");
            }
        }
        catch (Exception e)
        {
            if (!IsDisposed && !automatic) { status.Text = "更新检查/下载失败，可稍后重试。"; await UiMessageAsync( e.Message, "更新未完成"); }
        }
        finally { checkingUpdate = false; if (ownsBusy) busy = false; if (!IsDisposed) { Enabled = true; UpdateButtons(); } }
    }
}
