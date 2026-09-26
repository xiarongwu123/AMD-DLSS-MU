using System.Net;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly AccountApiClient accountClient = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) }, new AccountTokenStore());
    readonly System.Windows.Forms.Timer accountHeartbeat = new() { Interval = 30000 };
    RoundedButton? accountHeader;
    bool heartbeatRunning, accountRequestBusy, lastAccountOnline;
    int accountTransactionDepth;
    DownloadSession? activeAccountDownload;
    string accountFeedback = "登录后使用客户端功能。账户服务需要保持联网。";

    void InitializeAccountAccess()
    {
        accountClient.Changed += AccountStateChanged;
        accountHeartbeat.Tick += async (_, _) => await RefreshAccountHeartbeatAsync();
        accountHeartbeat.Start();
    }

    async Task RestoreAccountAsync()
    {
        bool restored = false;
        try
        {
            accountRequestBusy = true;
            if (await accountClient.RestoreAsync(lifetime.Token))
            {
                restored = true;
                accountFeedback = accountClient.StorageWarning ?? "登录状态已恢复。";
            }
            else accountFeedback = accountClient.StorageWarning ?? "请登录 MU 账户。";
        }
        catch (Exception e) { accountFeedback = AccountError(e); }
        finally { accountRequestBusy = false; if (!IsDisposed) { RenderAccount(); ApplyAccountGate(); } }
        if (restored && accountClient.IsOnline && !IsDisposed)
        {
            SwitchPage(0); await ScanGamesAsync();
            if (autoCheckUpdates && accountClient.IsOnline) await CheckAppUpdateAsync(true);
        }
    }

    async Task RefreshAccountHeartbeatAsync()
    {
        if (heartbeatRunning || accountRequestBusy || !accountClient.HasSession || IsDisposed) return;
        heartbeatRunning = true;
        try { await accountClient.HeartbeatAsync(lifetime.Token); }
        catch (Exception e) { accountFeedback = AccountError(e); }
        finally { heartbeatRunning = false; if (!IsDisposed) { ApplyAccountGate(); if (activePage == 6 && !accountRequestBusy && accountMode == "login") RenderAccount(); } }
    }

    void AccountStateChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)AccountStateChanged); return; }
        var lost = lastAccountOnline && !accountClient.IsOnline;
        lastAccountOnline = accountClient.IsOnline;
        if (lost)
        {
            accountMode = "login";
            accountFeedback = accountClient.HasSession ? "账户服务连接中断，联网验证成功后可继续使用。" : "登录已失效，请重新登录。";
            controlPanel?.Close();
            if (accountTransactionDepth == 0) activeAccountDownload?.Cancel();
        }
        if (accountClient.Account?.Features.FirstOrDefault(f => f.Key == "diagnostics.use") is { Allowed: false }) controlPanel?.Close();
        ApplyAccountGate();
    }

    void ApplyAccountGate()
    {
        if (IsDisposed || Disposing) return;
        if (accountHeader != null)
            accountHeader.Text = accountClient.Account == null ? "登录 / 注册" : accountClient.IsOnline
                ? accountClient.Account.Membership.Tier == "pro" ? "PRO · 账户" : "我的账户" : "重新连接";
        librarySearch.Enabled = accountClient.IsOnline;
        if (headerNavigation != null) headerNavigation.Enabled = accountClient.IsOnline;
        if (headerUpdate != null) headerUpdate.Enabled = accountClient.IsOnline;
        libraryPage.Enabled = accountClient.IsOnline && !busy;
        magpiePage.Enabled = accountClient.IsOnline && !busy;
        if (!accountClient.IsOnline && !busy && accountTransactionDepth == 0 && activePage != 6) SwitchPage(6);
    }

    async Task<bool> RequireFeatureAsync(string featureKey)
    {
        if (IsDisposed || lifetime.IsCancellationRequested) return false;
        if (!accountClient.HasSession)
        {
            accountFeedback = "请先登录，再使用客户端功能。";
            if (!accountRequestBusy) RenderAccount();
            SwitchPage(6); return false;
        }
        try { return await accountClient.AuthorizeAsync(featureKey, lifetime.Token) && !IsDisposed && !lifetime.IsCancellationRequested; }
        catch (Exception e)
        {
            if (IsDisposed || Disposing) return false;
            accountFeedback = AccountError(e); status.Text = accountFeedback;
            if (accountClient.IsOnline && e is AccountApiException { Status: HttpStatusCode.Forbidden })
                MessageBox.Show(this, accountFeedback, "功能权限");
            else if (!busy && accountTransactionDepth == 0) SwitchPage(6);
            if (activePage == 6 && !accountRequestBusy) RenderAccount();
            return false;
        }
    }

    async Task NavigateAuthorizedAsync(int page)
    {
        if (page == 6) { SwitchPage(6); if (!accountRequestBusy) RenderAccount(); return; }
        var feature = page switch { 7 => "magpie.launch", 4 => "diagnostics.use", 5 => "configuration.edit", _ => "library.manage" };
        if (await RequireFeatureAsync(feature)) SwitchPage(page);
    }

    IDisposable BeginAccountTransaction()
    {
        accountTransactionDepth++;
        return new AccountTransaction(() => { accountTransactionDepth--; ApplyAccountGate(); });
    }

    sealed class AccountTransaction(Action finished) : IDisposable
    {
        Action? finish = finished;
        public void Dispose() { var action = Interlocked.Exchange(ref finish, null); action?.Invoke(); }
    }

    static string AccountError(Exception error) => error switch
    {
        AccountApiException e => e.RetryAfterSeconds is > 0 ? $"{e.Message}（{e.RetryAfterSeconds} 秒后重试）" : e.Message,
        OperationCanceledException => "账户服务连接超时，请检查网络后重试。",
        HttpRequestException => "无法连接账户服务，请检查网络后重试。",
        _ => error.Message
    };
}
