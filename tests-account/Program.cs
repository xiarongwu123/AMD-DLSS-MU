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
