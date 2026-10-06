using System.Net;
using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly AccountApiClient accountClient = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) }, new AccountTokenStore());
    readonly System.Windows.Forms.Timer accountHeartbeat = new() { Interval = 30000 };
    CancellationTokenSource? activeHeartbeatRequest;
    RoundedButton? accountHeader;
    bool heartbeatRunning, accountRequestBusy, lastAccountViewAvailable;
    int accountTransactionDepth;
    DownloadSession? activeAccountDownload;
    string accountFeedback = "登录后使用客户端功能。账户服务需要保持联网。";

    void InitializeAccountAccess()
    {
        accountClient.Changed += AccountStateChanged;
        accountHeartbeat.Tick += async (_, _) => await RefreshAccountHeartbeatAsync();
    }

    void UpdateAccountHeartbeatSchedule()
    {
        if (IsDisposed || Disposing) return;
        if (activePage != 6 && accountClient.HasSession && !lifetime.IsCancellationRequested)
            accountHeartbeat.Start();
        else
        {
            accountHeartbeat.Stop();
            activeHeartbeatRequest?.Cancel();
        }
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
        if (activePage == 6 || heartbeatRunning || accountRequestBusy || !accountClient.HasSession || IsDisposed || Disposing) return;
        heartbeatRunning = true;
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        activeHeartbeatRequest = requestLifetime;
        var wasOffline = !accountClient.IsOnline;
        try
        {
            await accountClient.HeartbeatAsync(requestLifetime.Token);
            if (requestLifetime.IsCancellationRequested || activePage == 6 || IsDisposed || Disposing) return;
            if (wasOffline)
            {
                accountFeedback = accountClient.StorageWarning ?? "账户连接已恢复。";
                if (!busy && !accountRequestBusy) status.Text = accountFeedback;
            }
        }
        catch (OperationCanceledException) when (requestLifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (requestLifetime.IsCancellationRequested || activePage == 6 || IsDisposed || Disposing) return;
            accountFeedback = accountClient.IsReconnecting ? "账户连接暂时波动，正在重试…" : AccountError(e);
            if (!busy && !accountRequestBusy) status.Text = accountFeedback;
        }
        finally
        {
            activeHeartbeatRequest = null;
            heartbeatRunning = false;
            if (!IsDisposed && !Disposing) ApplyAccountGate();
        }
    }

    void AccountStateChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)AccountStateChanged); return; }
        var lost = lastAccountViewAvailable && !accountClient.CanKeepVerifiedView;
        lastAccountViewAvailable = accountClient.CanKeepVerifiedView;
        if (lost)
        {
            accountMode = "login";
            accountFeedback = accountClient.HasSession ? "账户服务连接中断，联网验证成功后可继续使用。" : "登录已失效，请重新登录。";
            controlPanel?.Close();
            if (accountTransactionDepth == 0) activeAccountDownload?.Cancel();
        }
        if (accountClient.Account?.Features.FirstOrDefault(f => f.Key == "diagnostics.use") is { Allowed: false }) controlPanel?.Close();
        ApplyAccountGate();
        if (lost && activePage == 6 && !accountRequestBusy) RenderAccount();
    }

    void ApplyAccountGate()
    {
        if (IsDisposed || Disposing) return;
        var viewAvailable = accountClient.CanKeepVerifiedView;
        if (accountHeader != null)
            accountHeader.Text = accountClient.Account == null ? "登录 / 注册" : accountClient.IsOnline
                ? accountClient.Account.Membership.Tier == "pro" ? "PRO · 账户" : "我的账户"
                : accountClient.IsReconnecting ? "连接重试中" : "重新连接";
        librarySearch.Enabled = viewAvailable;
        if (headerNavigation != null) headerNavigation.Enabled = viewAvailable;
        if (headerUpdate != null) headerUpdate.Enabled = viewAvailable;
        libraryPage.Enabled = viewAvailable && !busy;
        magpiePage.Enabled = viewAvailable && !busy;
        if (!viewAvailable && !busy && accountTransactionDepth == 0 && activePage != 6) SwitchPage(6);
        UpdateAccountHeartbeatSchedule();
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
            else if (!accountClient.CanKeepVerifiedView && !busy && accountTransactionDepth == 0) SwitchPage(6);
            else ShowProductToast(accountFeedback, false);
            if (activePage == 6 && !accountRequestBusy) RenderAccount();
            return false;
        }
    }

    bool CanNavigateToPage(int page)
    {
        if (IsDisposed || lifetime.IsCancellationRequested) return false;
        if (page == 6) return true;
        // Keep the existing ability to view/cancel an in-flight download when
        // connectivity is lost; this never authorizes starting new work.
        if (page == 3 && busy) return true;
        if (!accountClient.HasSession || !accountClient.CanKeepVerifiedView)
        {
            accountFeedback = "请先联网登录，再使用客户端功能。";
            SwitchPage(6); return false;
        }
        var feature = page switch { 7 => "magpie.launch", 4 => "diagnostics.use", 5 => "configuration.edit", _ => "library.manage" };
        if (accountClient.Account?.Features.Any(f => f.Key == feature && f.Allowed) != true)
        {
            ShowProductToast("当前账户暂无此页面的功能权限。", false); return false;
        }
        return true;
    }

    Task NavigateAuthorizedAsync(int page)
    {
        // Viewing an existing local page is not a privileged operation. Use the
        // heartbeat-verified snapshot so tab clicks cannot await/reorder HTTP
        // requests. Every action still calls RequireFeatureAsync server-side.
        if (CanNavigateToPage(page))
        {
            SwitchPage(page);
            if (page == 6 && !accountRequestBusy) RenderAccount();
        }
        return Task.CompletedTask;
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
        OperationCanceledException => "账户请求超时，请稍后重试；持续出现时请检查到服务端的网络连接。",
        HttpRequestException => "无法连接账户服务，请检查网络后重试。",
        _ => error.Message
    };
}
