using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AmdNrAssistant;

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    passed++; Console.WriteLine("PASS " + label);
}
var account = new AccountSnapshot("user-1", "player@example.com", new("standard", null),
    [new("library.manage", true, "standard", true)], false);
SessionResponse Session(string suffix = "1", bool expired = false) => new("access-" + suffix, "refresh-" + suffix,
    DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 15), account);
HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
HttpResponseMessage Error(HttpStatusCode status, string code, string message = "Denied") =>
    new(status) { Content = JsonContent.Create(new { code, message }) };
async Task Throws(Func<Task> action, Func<Exception, bool> expected, string label)
{
    try { await action(); throw new Exception("Expected failure: " + label); }
    catch (Exception e) { Check(expected(e), label); }
}

var requests = new List<(string Path, string? Bearer, string Body)>();
var store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(async request =>
{
    var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
    requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, body));
    return request.RequestUri.AbsolutePath.EndsWith("authorize") ? Ok(new AuthorizationResponse(true, null, account)) : Ok(Session());
})), store))
{
    await client.LoginAsync("player@example.com", "password123", true, default);
    Check(client.IsOnline && client.HasSession && client.Account?.Email == account.Email, "login establishes verified account");
    Check(store.Value == "refresh-1" && !store.Value.Contains("password"), "only refresh token persisted");
    await client.AuthorizeAsync("library.manage", default);
    Check(requests[1].Path == "/api/v1/account/authorize" && requests[1].Bearer == "access-1", "each operation sends bearer authorization");
    Check(requests[1].Body.Contains("library.manage"), "feature key sent to server");
    Check(requests[0].Bearer == null, "login has no bearer token");
}

store = new MemoryStore { Value = "old" };
using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(Ok(Session())))), store))
{
    await client.LoginAsync("player@example.com", "password123", false, default);
    Check(store.Value == null && client.HasSession, "session-only login erases old persisted token");
}

string? registration = null;
using (var client = new AccountApiClient(new HttpClient(new Handler(async request =>
{
    registration = await request.Content!.ReadAsStringAsync(); return Ok(Session());
})), new MemoryStore()))
{
    await client.RegisterAsync("player@example.com", "123456", "password123", false, default);
    using var json = JsonDocument.Parse(registration!);
    Check(json.RootElement.GetProperty("termsVersion").GetString() == "2026-09-26" && json.RootElement.GetProperty("code").GetString() == "123456", "register includes accepted terms version and code");
}

store = new MemoryStore { Value = "refresh-old" };
var refreshes = 0;
using (var client = new AccountApiClient(new HttpClient(new Handler(async request =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("refresh"))
    {
        refreshes++; await Task.Delay(20); return Ok(Session("rotated"));
    }
    return Ok(new AuthorizationResponse(true, null, account));
})), store))
{
    Check(await client.RestoreAsync(default), "remembered login restores via server");
    await Task.WhenAll(client.AuthorizeAsync("library.manage", default), client.AuthorizeAsync("library.manage", default));
    Check(refreshes == 1 && store.Value == "refresh-rotated", "concurrent operations preserve rotated refresh token");
}

store = new MemoryStore();
bool unavailable = false;
using (var client = new AccountApiClient(new HttpClient(new Handler(request =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Ok(Session()));
    if (unavailable) throw new HttpRequestException("offline");
    return Task.FromResult(Ok(account));
})), store))
{
    await client.LoginAsync(account.Email, "password123", true, default); unavailable = true;
    await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, "network failure surfaces without cached authorization");
    Check(!client.IsOnline && client.HasSession && store.Value == "refresh-1", "outage locks use while retaining refresh for recovery");
    unavailable = false; await client.HeartbeatAsync(default);
    Check(client.IsOnline, "successful server heartbeat recovers online state");
}

using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : Error(HttpStatusCode.Forbidden, "pro_required")))), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.AuthorizeAsync("dlss.configure", default), e => e is AccountApiException { Status: HttpStatusCode.Forbidden }, "pro denial fails operation");
    Check(client.IsOnline && client.HasSession, "ordinary feature denial keeps login");
}

foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
{
    store = new MemoryStore();
    using var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
        request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : Error(status, status == HttpStatusCode.Forbidden ? "account_disabled" : "session_invalid")))), store);
    await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is AccountApiException, "revocation/disabled returned by server");
    Check(!client.IsOnline && !client.HasSession && store.Value == null, "revocation/disabled erases session");
}

store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(request =>
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Task.FromResult(Ok(Session())) : throw new HttpRequestException("offline"))), store))
{
    await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => client.LogoutAsync(default), e => e is HttpRequestException, "logout reports unreachable server");
    Check(!client.HasSession && store.Value == null, "offline logout still clears local session");
}

store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : new HttpResponseMessage(HttpStatusCode.NoContent)))), store))
{
    await client.LoginAsync(account.Email, "password123", true, default);
    await client.ChangePasswordAsync("password123", "newpassword123", default);
    Check(!client.HasSession && store.Value == null, "successful password change requires new login");
}

var unauthorizedCalls = 0;
using (var client = new AccountApiClient(new HttpClient(new Handler(request =>
{
    unauthorizedCalls++; return Task.FromResult(Ok(account));
})), new MemoryStore()))
{
    await Throws(() => client.AuthorizeAsync("library.manage", default), e => e is AccountApiException { Code: "login_required" }, "anonymous operation rejected");
    Check(unauthorizedCalls == 0, "anonymous operation does not contact business API");
}

refreshes = 0;
store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(async request =>
{
    var path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("login")) return Ok(Session("expired", true));
    if (path.EndsWith("refresh")) { refreshes++; await Task.Delay(20); return Ok(Session("fresh")); }
    Check(request.Headers.Authorization?.Parameter == "access-fresh", "expired session operation uses new access token");
    return Ok(new AuthorizationResponse(true, null, account));
})), store))
{
    await client.LoginAsync(account.Email, "password123", true, default);
    await Task.WhenAll(client.AuthorizeAsync("library.manage", default), client.AuthorizeAsync("dlss.configure", default));
    Check(refreshes == 1 && store.Value == "refresh-fresh", "expired concurrent operations perform one rotation");
}

store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : Error(HttpStatusCode.ServiceUnavailable, "service_unavailable")))), store))
{
    await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => client.AuthorizeAsync("library.manage", default), e => e is AccountApiException { Status: HttpStatusCode.ServiceUnavailable }, "server outage denies new operation");
    Check(!client.IsOnline && client.HasSession && store.Value != null, "server outage retains only recovery session");
}

using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : Ok(new { id = "user-1", email = account.Email })))), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is IOException, "invalid account response fails closed");
    Check(!client.IsOnline, "invalid account response cannot leave verified online state");
}
var clock = new ManualClock();
var outage = true;
var authorizationCalls = 0;
store = new MemoryStore();
using (var client = new AccountApiClient(new HttpClient(new Handler(request =>
{
    var path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("login")) return Task.FromResult(Ok(Session()));
    if (path.EndsWith("authorize")) authorizationCalls++;
    if (outage) throw new HttpRequestException("connection reset");
    return Task.FromResult(path.EndsWith("authorize") ? Ok(new AuthorizationResponse(true, null, account)) : Ok(account));
})), store, clock))
{
    await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, "heartbeat interruption surfaces");
    Check(!client.IsOnline && client.IsReconnecting && client.CanKeepVerifiedView, "one interruption keeps the verified view during reconnection");
    await Throws(() => client.AuthorizeAsync("library.manage", default), e => e is HttpRequestException, "grace cannot authorize an operation from cached features");
    Check(authorizationCalls == 1, "operation during grace still contacts the server");
    clock.Advance(TimeSpan.FromSeconds(119));
    await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, "repeated heartbeat interruption surfaces");
    Check(client.CanKeepVerifiedView, "view remains available before the two-minute boundary");
    clock.Advance(TimeSpan.FromSeconds(1));
    Check(!client.CanKeepVerifiedView && !client.IsReconnecting, "retries do not extend grace past the two-minute boundary");
    Check(client.HasSession && store.Value != null, "grace expiry retains the recovery session");
    outage = false;
    await client.HeartbeatAsync(default);
    Check(client.IsOnline && client.CanKeepVerifiedView, "successful heartbeat recovers without another password login");
    outage = true;
    await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, "a later outage surfaces");
    clock.Advance(TimeSpan.FromSeconds(119));
    Check(client.CanKeepVerifiedView, "a verified recovery resets grace for a later outage");
}

foreach (var failure in new[] { "timeout", "unavailable", "rate_limit" })
{
    using var client = new AccountApiClient(new HttpClient(new Handler(request =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Ok(Session()));
        if (failure == "timeout") throw new TaskCanceledException("request timeout");
        return Task.FromResult(Error(failure == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests, failure));
    })), new MemoryStore(), new ManualClock());
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is OperationCanceledException or AccountApiException, failure + " surfaces without clearing login");
    Check(!client.IsOnline && client.CanKeepVerifiedView && client.HasSession, failure + " permits only the temporary verified view");
}

foreach (var failure in new[] { "revoked", "disabled", "invalid_response" })
{
    var phase = 0;
    store = new MemoryStore();
    using var client = new AccountApiClient(new HttpClient(new Handler(request =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Ok(Session()));
        if (phase is 0 or 2) throw new HttpRequestException("temporary outage");
        return Task.FromResult(failure == "invalid_response" ? Ok(new { id = "user-1" })
            : Error(failure == "disabled" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                failure == "disabled" ? "account_disabled" : "session_invalid"));
    })), store, new ManualClock());
    await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, failure + " fixture enters grace");
    phase = 1;
    await Throws(() => client.HeartbeatAsync(default), e => e is AccountApiException or IOException, failure + " surfaces during grace");
    Check(!client.CanKeepVerifiedView, failure + " ends grace immediately");
    if (failure != "invalid_response") Check(!client.HasSession && store.Value == null, failure + " clears credentials immediately");
    else
    {
        phase = 2;
        await Throws(() => client.HeartbeatAsync(default), e => e is HttpRequestException, "transport interruption after invalid response surfaces");
        Check(!client.CanKeepVerifiedView, "a hard failure cannot reopen grace without fresh server verification");
    }
}

using (var client = new AccountApiClient(new HttpClient(new Handler(_ => throw new HttpRequestException("offline"))),
    new MemoryStore { Value = "saved-refresh" }, new ManualClock()))
{
    await Throws(() => client.RestoreAsync(default), e => e is HttpRequestException, "unverified startup restoration fails");
    Check(!client.CanKeepVerifiedView, "saved credentials alone cannot grant a verified view");
}

using (var cancel = new CancellationTokenSource())
using (var client = new AccountApiClient(new HttpClient(new Handler(request =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Ok(Session()));
    cancel.Cancel(); throw new OperationCanceledException(cancel.Token);
})), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.HeartbeatAsync(cancel.Token), e => e is OperationCanceledException, "caller cancellation surfaces");
    Check(client.IsOnline && !client.IsReconnecting, "caller cancellation does not mark the connection offline");
}

foreach (bool restore in new[] { false, true })
{
    using var cancel = new CancellationTokenSource();
    var rotatedStore = new MemoryStore { Value = restore ? "refresh-before" : null };
    using var client = new AccountApiClient(new HttpClient(new TokenHandler(async (request, transportToken) =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Ok(Session("before", true));
        Check(request.RequestUri.AbsolutePath.EndsWith("refresh"), "cancelled rotation does not reach business endpoint");
        cancel.Cancel();
        await Task.Delay(10, transportToken);
        return Ok(Session("after"));
    })), rotatedStore);
    if (!restore) await client.LoginAsync(account.Email, "password123", true, default);
    await Throws(() => restore ? client.RestoreAsync(cancel.Token) : client.HeartbeatAsync(cancel.Token),
        e => e is OperationCanceledException, "page cancellation surfaces after rotation");
    Check(rotatedStore.Value == "refresh-after" && client.IsOnline && client.HasSession,
        "page cancellation preserves rotated credentials and verified state");
}

using (var client = new AccountApiClient(new HttpClient(new Handler(request => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session()) : new HttpResponseMessage(HttpStatusCode.NotFound)))), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.HeartbeatAsync(default), e => e is AccountApiException { Status: HttpStatusCode.NotFound }
        && e.Message.Contains("HTTP 404"), "empty 404 response reports missing endpoint");
    Check(client.IsOnline && client.HasSession, "missing endpoint does not invalidate login");
}

Console.WriteLine($"Account tests passed: {passed}");

sealed class MemoryStore : IAccountTokenStore
{
    public string? Value;
    public string? Read() => Value;
    public void Write(string refreshToken) => Value = refreshToken;
    public void Clear() => Value = null;
}
sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}
sealed class TokenHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
sealed class ManualClock : TimeProvider
{
    long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public void Advance(TimeSpan duration) => ticks += duration.Ticks;
}
