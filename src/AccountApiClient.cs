using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AmdNrAssistant;

public sealed record MembershipInfo(string Tier, DateTimeOffset? ExpiresAt, string Kind = "standard", DateTimeOffset? PlusExpiresAt = null, int? RemoteSessionsRemaining = null);
public sealed record FeatureAccess(string Key, bool Enabled, string MinimumTier, bool Allowed);
public sealed record AccountSnapshot(string Id, string Email, MembershipInfo Membership, FeatureAccess[] Features, bool PaymentsEnabled);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, AccountSnapshot Account);
public sealed record CodeResponse(string Message, int RetryAfterSeconds);
public sealed record AuthorizationResponse(bool Allowed, string? Reason, AccountSnapshot Account);
public sealed record PurchasePlan(string Id, string Name, int DurationDays, long PriceMinor, string Currency, int ConfigurationSessions = 0, bool GrantsMembership = true);
public sealed record PurchasePlans(PurchasePlan[] Items, bool PaymentsEnabled, string? PaymentMethod, bool CdkPlanMatching = false, ManualPaymentDetails? Payment = null);
public sealed record PurchasePaymentChannel(string Id, string Name, string QrImageUrl);
public sealed record ManualPaymentDetails(string Method, string Instructions, string? QrImageUrl, PurchasePaymentChannel[]? Channels = null);
public sealed record PurchaseOrder(string Id, string PlanId, string PlanName, int DurationDays, string Status,
    long AmountMinor, string Currency, long CreatedAt, long? PaidAt, ManualPaymentDetails? Payment, int ConfigurationSessions = 0, string ConfigurationServiceStatus = "none", int? ConfigurationSessionsUsed = null, bool GrantsMembership = true);
public sealed record PurchaseOrderList(PurchaseOrder[] Items, bool PaymentsEnabled);

public static class MembershipAccess
{
    public static bool IsAdvancedFeature(string key) => key is "schemes.premium.configure" or "schemes.game.view" or "games.supported.view";
    public static bool HasActiveMembership(AccountSnapshot? account, DateTimeOffset now) =>
        account?.Membership is { Tier: "pro" } member && (member.ExpiresAt == null || member.ExpiresAt > now);
    public static bool CanUse(AccountSnapshot? account, string key, DateTimeOffset now)
    {
        if (!HasActiveMembership(account, now)) return false;
        var feature = account!.Features.FirstOrDefault(f => f.Key == key);
        return feature != null ? feature.Enabled && feature.Allowed : IsAdvancedFeature(key);
    }
}

public sealed class AccountApiException : Exception
{
    public HttpStatusCode Status { get; }
    public string Code { get; }
    public int? RetryAfterSeconds { get; }
    public AccountSnapshot? Account { get; }
    public AccountApiException(HttpStatusCode status, string code, string message, int? retryAfterSeconds = null, AccountSnapshot? account = null)
        : base(message) { Status = status; Code = code; RetryAfterSeconds = retryAfterSeconds; Account = account; }
}

public sealed partial class AccountApiClient : IDisposable
{
    public const string DefaultBaseUrl = "https://mu-api.claude-api.cn/";
    public const string TermsVersion = "2026-09-26";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;
    readonly IAccountTokenStore store;
    readonly TimeProvider clock;
    readonly SemaphoreSlim gate = new(1, 1);
    long? connectionFailureStarted;
    bool connectionGraceEligible;
    public static readonly TimeSpan ConnectionGracePeriod = TimeSpan.FromMinutes(2);
    string? accessToken, refreshToken;
    DateTimeOffset accessExpiresAt;
    bool remember;
    public AccountSnapshot? Account { get; private set; }
    public bool IsOnline { get; private set; }
    public bool HasSession => refreshToken != null;
    // This permits retaining the view only; operations still require a server response.
    public bool CanKeepVerifiedView => IsOnline || (connectionGraceEligible && Account != null && HasSession
        && connectionFailureStarted is { } started && clock.GetElapsedTime(started) < ConnectionGracePeriod);
    public bool IsReconnecting => !IsOnline && CanKeepVerifiedView;
    public string? StorageWarning { get; private set; }
    public event Action? Changed;

    public AccountApiClient(HttpClient http, IAccountTokenStore store, TimeProvider? clock = null)
    {
        this.http = http; this.store = store;
        this.clock = clock ?? TimeProvider.System;
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
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { connectionGraceEligible = false; IsOnline = false; Changed?.Invoke(); throw; }
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

    public Task<AccountSnapshot> RedeemMembershipAsync(string code, CancellationToken token, string? planId = null) =>
        AuthenticatedAsync<AccountSnapshot>(HttpMethod.Post, "account/redeem", new { code, planId }, token);

    public async Task<bool> AuthorizeAsync(string featureKey, CancellationToken token)
    {
        AuthorizationResponse response;
        try { response = await AuthenticatedAsync<AuthorizationResponse>(HttpMethod.Post, "account/authorize", new { featureKey }, token); }
        catch (AccountApiException e) when (e.Code == "unknown_feature" && MembershipAccess.IsAdvancedFeature(featureKey)
            && MembershipAccess.HasActiveMembership(e.Account, clock.GetUtcNow())
            && !e.Account!.Features.Any(f => f.Key == featureKey))
        {
            // An older server can still authenticate membership and authorize the
            // original operation. Never use this path for an explicitly denied key.
            var legacyKey = featureKey == "schemes.premium.configure" ? "dlss.configure" : "configuration.edit";
            response = await AuthenticatedAsync<AuthorizationResponse>(HttpMethod.Post, "account/authorize", new { featureKey = legacyKey }, token);
            if (!MembershipAccess.CanUse(response.Account, featureKey, clock.GetUtcNow()))
                throw new AccountApiException(HttpStatusCode.Forbidden, "pro_required", "此功能需要有效的高级会员。", account: response.Account);
        }
        if (!response.Allowed) throw new AccountApiException(HttpStatusCode.Forbidden, "feature_denied", response.Reason ?? "当前账户没有此功能权限。", account: response.Account);
        return true;
    }

    public Task<JsonElement> GetSmartRulesAsync(string gameId, CancellationToken token) =>
        AuthenticatedAsync<JsonElement>(HttpMethod.Get, "render-rules?format=2&gameId=" + Uri.EscapeDataString(gameId.ToLowerInvariant()), null, token);

    public Task<JsonElement> GetNeuralRulesAsync(string gameId, CancellationToken token) =>
        AuthenticatedAsync<JsonElement>(HttpMethod.Get, "render-rules?gameId=" + Uri.EscapeDataString(gameId.ToLowerInvariant()), null, token);

    public async Task<JsonElement> GetSupportedGamesAsync(CancellationToken token)
    {
        var result = await AuthenticatedAsync<JsonElement>(HttpMethod.Get, "catalog/supported-games", null, token);
        var snapshot = result.GetProperty("account").Deserialize<AccountSnapshot>(Json)
            ?? throw new IOException("会员状态响应不完整。");
        ValidateAccount(snapshot);
        Account = snapshot;
        Changed?.Invoke();
        return result.GetProperty("catalog").Clone();
    }

    public Task<PurchasePlans> GetPurchasePlansAsync(CancellationToken token) =>
        SendAsync<PurchasePlans>(HttpMethod.Get, "plans", null, null, token);

    public Task<PurchaseOrder> CreatePurchaseOrderAsync(string planId, CancellationToken token) =>
        AuthenticatedAsync<PurchaseOrder>(HttpMethod.Post, "orders", new { planId }, token);

    public Task<PurchaseOrder> GetPurchaseOrderAsync(string orderId, CancellationToken token) =>
        AuthenticatedAsync<PurchaseOrder>(HttpMethod.Get, "orders/" + Uri.EscapeDataString(orderId), null, token);

    public Task SubmitPurchaseForReviewAsync(string orderId, CancellationToken token) =>
        AuthenticatedAsync<object?>(HttpMethod.Post, "orders/" + Uri.EscapeDataString(orderId) + "/review", null, token);

    public Task<PurchaseOrderList> GetPurchaseOrdersAsync(CancellationToken token) =>
        AuthenticatedAsync<PurchaseOrderList>(HttpMethod.Get, "orders", null, token);

    async Task<T> AuthenticatedAsync<T>(HttpMethod method, string route, object? body, CancellationToken token)
    {
        await gate.WaitAsync(token);
        var compatibilityRateLimited = false;
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
            catch (AccountApiException e) when (e.Status == HttpStatusCode.TooManyRequests && route.StartsWith("compatibility/", StringComparison.Ordinal))
            { compatibilityRateLimited = true; throw; }
            if (result is AccountSnapshot snapshot) { ValidateAccount(snapshot); Account = snapshot; }
            else if (result is AuthorizationResponse authorization) { ValidateAccount(authorization.Account); Account = authorization.Account; }
            ConnectionVerified();
            return result;
        }
        catch (AccountApiException e)
        {
            if (e.Account != null) Account = e.Account;
            if (e.Status == HttpStatusCode.Unauthorized || e.Code is "disabled" or "account_disabled" or "user_disabled") ClearLocal();
            else if (compatibilityRateLimited) ConnectionVerified();
            else if ((int)e.Status >= 500 || e.Status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                ConnectionFailed();
            else ConnectionVerified();
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { ConnectionFailed(); throw; }
        catch { connectionGraceEligible = false; IsOnline = false; Changed?.Invoke(); throw; }
        finally { gate.Release(); }
    }

    async Task RefreshCoreAsync(CancellationToken token)
    {
        try
        {
            if (refreshToken == null) throw new AccountApiException(HttpStatusCode.Unauthorized, "login_required", "请重新登录账户。");
            token.ThrowIfCancellationRequested();
            // Rotation consumes the old token; persist the replacement even after a page switch.
            using var rotationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ApplySession(await SendAsync<SessionResponse>(HttpMethod.Post, "auth/refresh", new { refreshToken }, null, rotationTimeout.Token));
            token.ThrowIfCancellationRequested();
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
        ConnectionVerified();
    }

    void ConnectionVerified()
    {
        connectionFailureStarted = null;
        connectionGraceEligible = true;
        IsOnline = true; Changed?.Invoke();
    }

    void ConnectionFailed()
    {
        if (connectionGraceEligible) connectionFailureStarted ??= clock.GetTimestamp();
        IsOnline = false; Changed?.Invoke();
    }

    void ClearLocal()
    {
        connectionFailureStarted = null;
        connectionGraceEligible = false;
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
        if(route.StartsWith("render-rules?",StringComparison.Ordinal))
        {
            var actual=response.RequestMessage?.RequestUri;
            var origin=http.BaseAddress!;
            if(actual!=null&&(actual.Scheme!="https"||actual.Host!=origin.Host||actual.Port!=origin.Port))throw new IOException("规则响应来源不可信，拒绝重定向。");
            if(response.Content.Headers.ContentLength>1024*1024)throw new IOException("规则响应过大。");
        }
        var content = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = JsonSerializer.Deserialize<ApiError>(content, Json); } catch (JsonException) { }
            throw new AccountApiException(response.StatusCode, error?.Code ?? "service_error",
                error?.Message ?? ResponseError(response.StatusCode, route), error?.RetryAfterSeconds, error?.Account);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        try { return JsonSerializer.Deserialize<T>(content, Json) ?? throw new IOException("账户服务返回空响应。"); }
        catch (JsonException e) { throw new IOException("账户服务响应无效，请稍后重试。", e); }
    }

    static string ResponseError(HttpStatusCode status, string route) => status switch
    {
        HttpStatusCode.NotFound when route.StartsWith("presets/all", StringComparison.Ordinal) => "全站玩家方案需要更新后台服务，请联系管理员同步部署。",
        HttpStatusCode.NotFound when route.StartsWith("render-rules", StringComparison.Ordinal) => "游戏专属推荐需要更新后台服务；你的会员身份未改变。",
        HttpStatusCode.NotFound when route.StartsWith("catalog/supported-games", StringComparison.Ordinal) => "支持游戏库需要更新后台服务；你的会员身份未改变。",
        HttpStatusCode.NotFound when route.StartsWith("wishes", StringComparison.Ordinal) => "许愿池服务尚未上线，请稍后再试（HTTP 404）。",
        HttpStatusCode.NotFound when route.StartsWith("presets", StringComparison.Ordinal) => "社区方案后台尚未更新，请稍后再试（HTTP 404）。",
        HttpStatusCode.NotFound => "请求的服务接口不存在（HTTP 404），请检查客户端版本。",
        HttpStatusCode.Unauthorized => "登录已失效，请重新登录（HTTP 401）。",
        HttpStatusCode.Forbidden => "服务拒绝了此请求（HTTP 403）。",
        HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后重试（HTTP 429）。",
        _ => $"服务请求失败（HTTP {(int)status}），请稍后重试。"
    };

    sealed record ApiError(string Code, string Message, int? RetryAfterSeconds, AccountSnapshot? Account);
    public void Dispose() => http.Dispose();
}
