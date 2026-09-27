using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mu.Server;
using Mu.Server.Services;

internal static class HttpTests
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Main()
    {
        using var factory = new AccountFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Check((await client.GetAsync("/api/health")).IsSuccessStatusCode, "HTTP readiness");
        Check((await client.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous account blocked");
        Check((await client.PostAsJsonAsync("/api/v1/account/authorize", new { featureKey = "game.launch" })).StatusCode == HttpStatusCode.Unauthorized,
            "Anonymous business authorization blocked");
        const string email = "http-test@example.test";
        const string password = "correct horse battery";
        var codeResponse = await client.PostAsJsonAsync("/api/v1/auth/code", new { email, purpose = "register" });
        Check(codeResponse.IsSuccessStatusCode, "Verification email endpoint");
        Check(factory.Mail.Codes.TryGetValue(email + ":register", out var code), "Email sender invoked");
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
        { email, code, password, termsVersion = "2026-09-26", deviceName = "HTTP integration" });
        Check(registration.IsSuccessStatusCode, "Register through HTTP");
        var session = (await registration.Content.ReadFromJsonAsync<SessionResponse>())!;
        Check(session.Account.Membership.Tier == "standard" && !session.Account.PaymentsEnabled, "Default standard and payment disabled");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Check((await client.GetAsync("/api/v1/account/me")).IsSuccessStatusCode, "Bearer authentication works");
        Check((await client.PostAsJsonAsync("/api/v1/account/authorize", new { featureKey = "game.launch" })).IsSuccessStatusCode,
            "Initial feature available");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountManagementService>()
                .SetFeatureAsync("game.launch", true, "pro", "HTTP test", "test-admin");
        var denied = await client.PostAsJsonAsync("/api/v1/account/authorize", new { featureKey = "game.launch" });
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "Live policy blocks standard account");
        Check((await denied.Content.ReadFromJsonAsync<ApiError>())?.Code == "pro_required", "Stable denial contract");
        Check((await client.GetAsync("/api/v1/orders/not-owned")).StatusCode == HttpStatusCode.NotFound, "Order ownership boundary");
        Check((await client.PostAsJsonAsync("/api/v1/orders", new { planId = "anything" })).StatusCode == HttpStatusCode.ServiceUnavailable,
            "Payments fail closed");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Client token is not an admin session");
        Check((await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = email, ["Password"] = password }))).StatusCode == HttpStatusCode.BadRequest, "Admin login requires CSRF token");
        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken });
        Check(refreshed.IsSuccessStatusCode, "HTTP refresh");
        var rotated = (await refreshed.Content.ReadFromJsonAsync<SessionResponse>())!;
        Check((await client.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "Rotated access token invalidated");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Check((await client.GetAsync("/api/v1/account/me")).IsSuccessStatusCode, "New access token valid");
        Check((await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode == HttpStatusCode.Unauthorized,
            "Refresh replay rejected");
        Check((await client.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "Refresh replay revokes new family token");
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, deviceName = "HTTP integration" });
        Check(login.IsSuccessStatusCode, "Login after family revocation");
        var current = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
        Check((await client.PostAsJsonAsync("/api/v1/account/password", new { currentPassword = password, newPassword = "another correct password" })).StatusCode == HttpStatusCode.NoContent,
            "Password change endpoint");
        Check((await client.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "Password change revokes session immediately");
        Check((await client.GetAsync("/legal/terms")).IsSuccessStatusCode && (await client.GetAsync("/legal/privacy")).IsSuccessStatusCode,
            "Public agreements available before login");
        assertions += await EmailProviderTests.RunAsync();
        Console.WriteLine($"HTTP integration passed: {assertions} assertions.");
    }
}

internal sealed class AccountFactory(Func<IServiceProvider, IVerificationEmailSender>? emailSenderFactory = null) : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mu-http-" + Guid.NewGuid().ToString("N"));
    public CaptureMail Mail { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", Path.Combine(directory, "accounts.sqlite"));
        builder.UseSetting("DataProtection:Path", Path.Combine(directory, "keys"));
        builder.UseSetting("Auth:CodePepper", "integration-test-only-pepper-with-at-least-32-bytes");
        builder.UseSetting("Registration:Enabled", "true");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVerificationEmailSender>();
            if (emailSenderFactory is null) services.AddSingleton<IVerificationEmailSender>(Mail);
            else services.AddSingleton(emailSenderFactory);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

internal sealed class CaptureMail : IVerificationEmailSender
{
    public ConcurrentDictionary<string, string> Codes { get; } = new();
    public Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default)
    {
        Codes[email + ":" + purpose] = code;
        return Task.CompletedTask;
    }
}
