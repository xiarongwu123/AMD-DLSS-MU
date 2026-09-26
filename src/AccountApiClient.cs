using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AmdNrAssistant;

public sealed record MembershipInfo(string Tier, DateTimeOffset? ExpiresAt);
public sealed record FeatureAccess(string Key, bool Enabled, string MinimumTier, bool Allowed);
public sealed record AccountSnapshot(string Id, string Email, MembershipInfo Membership, FeatureAccess[] Features, bool PaymentsEnabled);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, AccountSnapshot Account);
public sealed record CodeResponse(string Message, int RetryAfterSeconds);
public sealed record AuthorizationResponse(bool Allowed, string? Reason, AccountSnapshot Account);

public sealed class AccountApiException : Exception
{
    public HttpStatusCode Status { get; }
    public string Code { get; }
    public int? RetryAfterSeconds { get; }
    public AccountSnapshot? Account { get; }
    public AccountApiException(HttpStatusCode status, string code, string message, int? retryAfterSeconds = null, AccountSnapshot? account = null)
        : base(message) { Status = status; Code = code; RetryAfterSeconds = retryAfterSeconds; Account = account; }
}

public sealed class AccountApiClient : IDisposable
{
    public const string DefaultBaseUrl = "https://mu-api.claude-api.cn/";
    public const string TermsVersion = "2026-09-26";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;
    readonly IAccountTokenStore store;
    readonly SemaphoreSlim gate = new(1, 1);
    string? accessToken, refreshToken;
    DateTimeOffset accessExpiresAt;
    bool remember;
    public AccountSnapshot? Account { get; private set; }
    public bool IsOnline { get; private set; }
    public bool HasSession => refreshToken != null;
    public string? StorageWarning { get; private set; }
    public event Action? Changed;

    public AccountApiClient(HttpClient http, IAccountTokenStore store)
    {
        this.http = http; this.store = store;
        http.BaseAddress ??= new Uri(DefaultBaseUrl);
    }

    public async Task<bool> RestoreAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            try { refreshToken = store.Read(); remember = refreshToken != null; }
            catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
            { ClearLocal(); StorageWarning = "保存的登录信息不可用，请重新登录。"; return false; }
            if (refreshToken == null) return false;
            await RefreshCoreAsync(token);
            return true;
        }
        catch { IsOnline = false; Changed?.Invoke(); throw; }
        finally { gate.Release(); }
    }

    public Task<CodeResponse> SendCodeAsync(string email, string purpose, CancellationToken token) =>
        SendAsync<CodeResponse>(HttpMethod.Post, "auth/code", new { email, purpose }, null, token);

    public Task RegisterAsync(string email, string code, string password, bool keep, CancellationToken token) =>
        StartSessionAsync("auth/register", new { email, code, password, termsVersion = TermsVersion, deviceName = Environment.MachineName }, keep, token);

    public Task LoginAsync(string email, string password, bool keep, CancellationToken token) =>
        StartSessionAsync("auth/login", new { email, password, deviceName = Environment.MachineName }, keep, token);

    async Task StartSessionAsync(string route, object body, bool keep, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var session = await SendAsync<SessionResponse>(HttpMethod.Post, route, body, null, token);
            remember = keep;
            ApplySession(session);
        }
        finally { gate.Release(); }
    }

    public Task ResetPasswordAsync(string email, string code, string newPassword, CancellationToken token) =>
        SendAsync<object?>(HttpMethod.Post, "auth/reset-password", new { email, code, newPassword }, null, token);

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken token)
    {
        await AuthenticatedAsync<object?>(HttpMethod.Post, "account/password", new { currentPassword, newPassword }, token);
        await gate.WaitAsync(token);
        try { ClearLocal(); }
        finally { gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (refreshToken != null)
            {
                if (accessToken == null || accessExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30)) await RefreshCoreAsync(token);
                await SendAsync<object?>(HttpMethod.Post, "auth/logout", null, accessToken, token);
            }
        }
        finally { ClearLocal(); gate.Release(); }
    }

    public async Task<AccountSnapshot> HeartbeatAsync(CancellationToken token)
    {
        return await AuthenticatedAsync<AccountSnapshot>(HttpMethod.Get, "account/me", null, token);
    }

    public async Task<bool> AuthorizeAsync(string featureKey, CancellationToken token)
    {
        var response = await AuthenticatedAsync<AuthorizationResponse>(HttpMethod.Post, "account/authorize", new { featureKey }, token);
        if (!response.Allowed) throw new AccountApiException(HttpStatusCode.Forbidden, "feature_denied", response.Reason ?? "当前账户没有此功能权限。", account: response.Account);
        return true;
    }

    async Task<T> AuthenticatedAsync<T>(HttpMethod method, string route, object? body, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (refreshToken == null) throw new AccountApiException(HttpStatusCode.Unauthorized, "login_required", "请先登录账户。");
            if (accessToken == null || accessExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30)) await RefreshCoreAsync(token);
            T result;
            try { result = await SendAsync<T>(method, route, body, accessToken, token); }
            catch (AccountApiException e) when (e.Status == HttpStatusCode.Unauthorized)
            {
                await RefreshCoreAsync(token);
                result = await SendAsync<T>(method, route, body, accessToken, token);
            }
            if (result is AccountSnapshot snapshot) { ValidateAccount(snapshot); Account = snapshot; }
            else if (result is AuthorizationResponse authorization) { ValidateAccount(authorization.Account); Account = authorization.Account; }
            IsOnline = true; Changed?.Invoke();
            return result;
        }
        catch (AccountApiException e)
        {
            if (e.Account != null) Account = e.Account;
            if (e.Status == HttpStatusCode.Unauthorized || e.Code is "disabled" or "account_disabled" or "user_disabled") ClearLocal();
            else { IsOnline = e.Status == HttpStatusCode.Forbidden; Changed?.Invoke(); }
            throw;
        }
        catch { IsOnline = false; Changed?.Invoke(); throw; }
        finally { gate.Release(); }
    }

    async Task RefreshCoreAsync(CancellationToken token)
    {
        try
        {
            if (refreshToken == null) throw new AccountApiException(HttpStatusCode.Unauthorized, "login_required", "请重新登录账户。");
            ApplySession(await SendAsync<SessionResponse>(HttpMethod.Post, "auth/refresh", new { refreshToken }, null, token));
        }
        catch (AccountApiException e) when (e.Status == HttpStatusCode.Unauthorized || e.Code is "disabled" or "account_disabled" or "user_disabled")
        { ClearLocal(); throw; }
    }

    void ApplySession(SessionResponse session)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken) || string.IsNullOrWhiteSpace(session.RefreshToken) || session.Account == null)
            throw new IOException("账户服务返回了无效的登录信息。");
        ValidateAccount(session.Account);
        accessToken = session.AccessToken; refreshToken = session.RefreshToken;
        accessExpiresAt = session.AccessTokenExpiresAt; Account = session.Account;
        StorageWarning = null;
        try { if (remember) store.Write(refreshToken); else store.Clear(); }
        catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            remember = false;
            try { store.Clear(); } catch { }
            StorageWarning = "登录成功，但无法保存保持登录状态；退出程序后需要重新登录。";
        }
        IsOnline = true; Changed?.Invoke();
    }

    void ClearLocal()
    {
        accessToken = refreshToken = null; Account = null; IsOnline = false; remember = false;
        try { store.Clear(); } catch { StorageWarning = "未能删除本机登录缓存，请检查账户数据目录的写入权限。"; }
        Changed?.Invoke();
    }

    static void ValidateAccount(AccountSnapshot account)
    {
        if (account == null || string.IsNullOrWhiteSpace(account.Id) || string.IsNullOrWhiteSpace(account.Email)
            || account.Membership == null || account.Membership.Tier is not ("standard" or "pro") || account.Features == null)
            throw new IOException("账户服务返回了无效的账户状态。");
    }

    async Task<T> SendAsync<T>(HttpMethod method, string route, object? body, string? bearer, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, "api/v1/" + route);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request, token);
        var content = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = JsonSerializer.Deserialize<ApiError>(content, Json); } catch (JsonException) { }
            throw new AccountApiException(response.StatusCode, error?.Code ?? "service_error",
                error?.Message ?? "账户服务暂时不可用，请稍后重试。", error?.RetryAfterSeconds, error?.Account);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        try { return JsonSerializer.Deserialize<T>(content, Json) ?? throw new IOException("账户服务返回空响应。"); }
        catch (JsonException e) { throw new IOException("账户服务响应无效，请稍后重试。", e); }
    }

    sealed record ApiError(string Code, string Message, int? RetryAfterSeconds, AccountSnapshot? Account);
    public void Dispose() => http.Dispose();
}
