using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server;
using Mu.Server.Data;

internal static class AdminConsoleTests
{
    private const string AdminEmail = "admin@example.test";
    private const string UserEmail = "ordinary@example.test";
    private const string Password = "admin integration password";
    private static int assertions;

    public static async Task Main()
    {
        using var factory = new AdminFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        string adminId, userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Check((await roles.CreateAsync(new(AdminSecurity.Role))).Succeeded, "Create admin role");
            var admin = NewUser(AdminEmail);
            Check((await users.CreateAsync(admin, Password)).Succeeded, "Create admin");
            Check((await users.AddToRoleAsync(admin, AdminSecurity.Role)).Succeeded, "Grant separate admin role");
            var user = NewUser(UserEmail);
            Check((await users.CreateAsync(user, Password)).Succeeded, "Create ordinary user");
            adminId = admin.Id;
            userId = user.Id;
        }

        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Anonymous administrator access blocked");
        Check((await Post(client, "/admin/login", new() { ["Email"] = AdminEmail, ["Password"] = Password })).StatusCode == HttpStatusCode.BadRequest,
            "Admin login requires CSRF");
        var loginToken = await Token(client, "/admin/login");
        Check((await Post(client, "/admin/login", new() { ["Email"] = UserEmail, ["Password"] = Password }, loginToken)).StatusCode == HttpStatusCode.OK,
            "Ordinary user credentials cannot sign into admin");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Ordinary account has no admin cookie");
        var passwordStep = await Post(client, "/admin/login", new() { ["Email"] = AdminEmail, ["Password"] = Password }, loginToken);
        Check(passwordStep.StatusCode == HttpStatusCode.Redirect && passwordStep.Headers.Location?.OriginalString == "/admin/setup", "First admin login requires TOTP setup");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Password-only pending cookie cannot manage");
        var setup = await client.GetAsync("/admin/setup");
        var setupHtml = await setup.Content.ReadAsStringAsync();
        var key = Regex.Match(setupHtml, "<code class=\"secret\">([^<]+)</code>").Groups[1].Value;
        Check(key.Length >= 16, "Authenticator enrollment key displayed locally");
        Check(setup.Headers.CacheControl?.NoStore == true, "Authenticator setup never cached");
        var enrollmentCode = Totp(key);
        var setupPost = await Post(client, "/admin/setup", new() { ["Code"] = enrollmentCode }, ExtractToken(setupHtml));
        Check(setupPost.StatusCode == HttpStatusCode.Redirect && setupPost.Headers.Location?.OriginalString == "/admin", "TOTP enrollment grants full admin session");
        var adminCookie = setupPost.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-MuAdmin=", StringComparison.Ordinal)).Split(';')[0];
        Check(setupPost.Headers.GetValues("Set-Cookie").Any(value => value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)), "Admin cookie protected");
        foreach (var path in new[] { "/admin", "/admin/users", "/admin/features", "/admin/plans", "/admin/orders", "/admin/audit" })
            Check((await client.GetAsync(path)).IsSuccessStatusCode, "Authorized page renders: " + path);

        Check((await Post(client, "/admin/users?handler=Grant", new() { ["UserId"] = userId, ["Days"] = "30", ["Reason"] = "test grant" })).StatusCode == HttpStatusCode.BadRequest,
            "Management mutations require CSRF");
        Check((await Form(client, "/admin/users", "Grant", new() { ["UserId"] = userId, ["Days"] = "30", ["Reason"] = "test grant" })).StatusCode == HttpStatusCode.Redirect,
            "Grant membership through actual Razor form");
        long firstExpiry;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            firstExpiry = (await db.Users.SingleAsync(user => user.Id == userId)).ProExpiresAt!.Value;
            Check(firstExpiry >= DateTimeOffset.UtcNow.AddDays(29).ToUnixTimeSeconds(), "Membership grant persisted");
        }
        Check((await Form(client, "/admin/users", "Grant", new() { ["UserId"] = userId, ["Days"] = "15", ["Reason"] = "test extension" })).StatusCode == HttpStatusCode.Redirect,
            "Extend membership through actual form");
        using (var scope = factory.Services.CreateScope())
            Check((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(user => user.Id == userId)).ProExpiresAt == firstExpiry + 15 * 86400L,
                "Extension starts at existing expiry");
        Check((await Form(client, "/admin/features", null, new() { ["Key"] = "game.launch", ["Access"] = "pro", ["Reason"] = "test pro feature" })).StatusCode == HttpStatusCode.Redirect,
            "Change feature requirement through form");
        Check((await Form(client, "/admin/plans", null, new() { ["Name"] = "测试套餐", ["DurationDays"] = "30", ["Price"] = "19.90", ["Enabled"] = "true", ["Reason"] = "test creation" })).StatusCode == HttpStatusCode.Redirect,
            "Create plan through form");
        string planId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var plan = await db.Plans.SingleAsync();
            planId = plan.Id;
            Check(plan.PriceMinor == 1990 && !plan.Enabled, "Plan price normalized and new plan starts unpublished");
            Check((await db.FeatureDefinitions.SingleAsync(feature => feature.Key == "game.launch")).MinimumTier == "pro", "Feature policy persisted");
            Check(await db.AuditEntries.AnyAsync(entry => entry.Action == "plan.created")
                && await db.AuditEntries.AnyAsync(entry => entry.Action == "membership.expiry")
                && await db.AuditEntries.AnyAsync(entry => entry.Action == "feature.configure"), "Form actions audited");
        }
        Check((await Form(client, "/admin/plans", null, new() { ["Id"] = planId, ["Name"] = "修改套餐", ["DurationDays"] = "60", ["Price"] = "123.45", ["Enabled"] = "true", ["Reason"] = "test edit" })).StatusCode == HttpStatusCode.Redirect,
            "Edit plan through form");
        Check((await Form(client, "/admin/plans", null, new() { ["Id"] = planId, ["Name"] = "invalid price", ["DurationDays"] = "30", ["Price"] = "1.001", ["Reason"] = "test invalid" })).StatusCode == HttpStatusCode.OK,
            "Sub-cent prices rejected without mutation");
        using (var scope = factory.Services.CreateScope())
        {
            var plan = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Plans.SingleAsync();
            Check(plan.PriceMinor == 12345 && plan.DurationDays == 60 && plan.Enabled, "Plan edit persisted and invalid edit ignored");
        }
        Check((await Form(client, "/admin/users", "Disable", new() { ["UserId"] = adminId, ["Reason"] = "test disable admin" })).StatusCode == HttpStatusCode.OK,
            "User management cannot disable an administrator");

        using var normal = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var login = await normal.PostAsJsonAsync("/api/v1/auth/login", new { email = UserEmail, password = Password, deviceName = "admin-test" });
        Check(login.IsSuccessStatusCode, "Ordinary client login");
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        normal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Check((await normal.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Client bearer cannot authenticate administrator page");
        Check((await Form(client, "/admin/users", "RevokeSessions", new() { ["UserId"] = userId, ["Reason"] = "test revoke sessions" })).StatusCode == HttpStatusCode.Redirect,
            "Session revocation form");
        Check((await normal.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "Admin revocation invalidates client immediately");
        Check((await Form(client, "/admin/users", "Disable", new() { ["UserId"] = userId, ["Reason"] = "test disable" })).StatusCode == HttpStatusCode.Redirect, "Disable user form");
        Check((await normal.PostAsJsonAsync("/api/v1/auth/login", new { email = UserEmail, password = Password })).StatusCode == HttpStatusCode.Forbidden,
            "Disabled user cannot log in");
        Check((await Form(client, "/admin/users", "Enable", new() { ["UserId"] = userId, ["Reason"] = "test enable" })).StatusCode == HttpStatusCode.Redirect, "Enable user form");
        Check((await Form(client, "/admin/users", "RevokePro", new() { ["UserId"] = userId, ["Reason"] = "test revoke pro" })).StatusCode == HttpStatusCode.Redirect, "Revoke membership form");
        using (var scope = factory.Services.CreateScope())
            Check((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(user => user.Id == userId)).ProExpiresAt is null, "Membership revoked in database");

        Check((await Post(client, "/admin/logout", [], await Token(client, "/admin/users"))).StatusCode == HttpStatusCode.Redirect, "Logout form");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Logout removes admin session");
        Check((await Post(client, "/admin/login", new() { ["Email"] = AdminEmail, ["Password"] = Password }, await Token(client, "/admin/login"))).Headers.Location?.OriginalString == "/admin/verify",
            "Subsequent login requires enrolled TOTP");
        var verifyToken = await Token(client, "/admin/verify");
        Check((await Post(client, "/admin/verify", new() { ["Code"] = enrollmentCode }, verifyToken)).StatusCode == HttpStatusCode.OK, "Used TOTP rejected");
        Check((await Post(client, "/admin/verify", new() { ["Code"] = Totp(key, 30) }, verifyToken)).StatusCode == HttpStatusCode.Redirect, "New TOTP completes subsequent login");

        using var replay = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", adminCookie);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.RemoveFromRoleAsync((await users.FindByIdAsync(adminId))!, AdminSecurity.Role);
        }
        Check((await replay.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Role removal invalidates existing admin cookie");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByIdAsync(adminId))!;
            await users.AddToRoleAsync(admin, AdminSecurity.Role);
            admin.DisabledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await users.UpdateAsync(admin);
        }
        Check((await replay.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Disabled administrator cookie rejected");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByIdAsync(adminId))!;
            admin.DisabledAt = null;
            await users.UpdateAsync(admin);
            await users.UpdateSecurityStampAsync(admin);
        }
        Check((await replay.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Security stamp rotation invalidates admin cookie");
        assertions += await BackupAtomicTests.RunAsync(factory.Services);
        Console.WriteLine($"Admin and backup integration passed: {assertions} assertions.");
    }

    private static ApplicationUser NewUser(string email) => new() { Email = email, UserName = email, EmailConfirmed = true,
        LockoutEnabled = true, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TermsVersion = "2026-09-26", TermsAcceptedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task<string> Token(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Check(response.IsSuccessStatusCode, "Get form: " + path);
        return ExtractToken(await response.Content.ReadAsStringAsync());
    }
    private static string ExtractToken(string html)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Check(token.Length > 0, "Form emits anti-forgery token");
        return WebUtility.HtmlDecode(token);
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields, string? token = null)
    {
        if (token is not null) fields["__RequestVerificationToken"] = token;
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    private static async Task<HttpResponseMessage> Form(HttpClient client, string path, string? handler, Dictionary<string, string> fields) =>
        await Post(client, path + (handler is null ? "" : "?handler=" + handler), fields, await Token(client, path));
    private static string Totp(string base32, int offsetSeconds = 0)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in base32.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) continue;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        var counter = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds) / 30;
        var payload = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(payload);
        var hash = HMACSHA1.HashData(bytes.ToArray(), payload);
        var offset = hash[^1] & 15;
        var valueCode = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (valueCode % 1000000).ToString("D6");
    }
}

internal sealed class AdminFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mu-admin-test-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", Path.Combine(directory, "accounts.sqlite"));
        builder.UseSetting("DataProtection:Path", Path.Combine(directory, "keys"));
        builder.UseSetting("Auth:CodePepper", "admin-integration-pepper-at-least-thirty-two-bytes");
        builder.UseSetting("Registration:Enabled", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
