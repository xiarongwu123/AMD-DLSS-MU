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
using Mu.Wishes;

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

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            db.GameTelemetry.AddRange(
                new GameTelemetryEntry { Id = Guid.NewGuid().ToString(), UserId = userId, GameName = "Cyberpunk <test>",
                    GpuName = "RX 7900", MuMode = "DLSS", StartedAt = now - 1800, EndedAt = now - 1200,
                    FrameCount = 600, AverageFps = 90, OnePercentLowFps = 60, PayloadJson = "{}", PayloadHash = "first" },
                new GameTelemetryEntry { Id = Guid.NewGuid().ToString(), UserId = userId, GameName = "RE9",
                    GpuName = "RX 6600", MuMode = "none", StartedAt = now - 1000, EndedAt = now - 600,
                    FrameCount = 0, PayloadJson = "{}", PayloadHash = "second" },
                new GameTelemetryEntry { Id = Guid.NewGuid().ToString(), UserId = userId, GameName = "Old game",
                    GpuName = "RX 580", MuMode = "none", StartedAt = now - 61 * 86400, EndedAt = now - 60 * 86400,
                    FrameCount = 400, AverageFps = 30, OnePercentLowFps = 20, PayloadJson = "{}", PayloadHash = "third" });
            await db.SaveChangesAsync();
        }

        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Anonymous administrator access blocked");
        Check((await client.GetAsync("/admin/mail")).StatusCode == HttpStatusCode.Redirect, "Anonymous mail logs blocked");
        Check((await client.GetAsync("/admin/telemetry")).StatusCode == HttpStatusCode.Redirect, "Anonymous telemetry report blocked");
        Check((await client.GetAsync("/admin/wishes")).StatusCode == HttpStatusCode.Redirect, "Anonymous wish management blocked");
        Check((await Post(client, "/admin/login", new() { ["Email"] = AdminEmail, ["Password"] = Password })).StatusCode == HttpStatusCode.BadRequest,
            "Admin login requires CSRF");
        var loginToken = await Token(client, "/admin/login");
        Check((await Post(client, "/admin/login", new() { ["Email"] = UserEmail, ["Password"] = Password }, loginToken)).StatusCode == HttpStatusCode.OK,
            "Ordinary user credentials cannot sign into admin");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Ordinary account has no admin cookie");
        Check((await client.GetAsync("/admin/mail")).StatusCode == HttpStatusCode.Redirect, "Ordinary account cannot read mail logs");
        var passwordStep = await Post(client, "/admin/login", new() { ["Email"] = AdminEmail, ["Password"] = Password }, loginToken);
        Check(passwordStep.StatusCode == HttpStatusCode.Redirect && passwordStep.Headers.Location?.OriginalString == "/admin/setup", "First admin login requires TOTP setup");
        Check((await client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.Redirect, "Password-only pending cookie cannot manage");
        Check((await client.GetAsync("/admin/mail")).StatusCode == HttpStatusCode.Redirect, "Mail logs require completed MFA");
        Check((await client.GetAsync("/admin/telemetry")).StatusCode == HttpStatusCode.Redirect, "Password-only cookie cannot read telemetry");
        Check((await client.GetAsync("/admin/wishes")).StatusCode == HttpStatusCode.Redirect, "Wish management requires completed MFA");
        var setup = await client.GetAsync("/admin/setup");
        var setupHtml = await setup.Content.ReadAsStringAsync();
        var key = Regex.Match(setupHtml, "<code class=\"secret\">([^<]+)</code>").Groups[1].Value;
        Check(key.Length >= 16, "Authenticator enrollment key displayed locally");
        Check(setup.Headers.CacheControl?.NoStore == true, "Authenticator setup never cached");
        var enrollmentCode = Totp(key);
        var setupPost = await Post(client, "/admin/setup", new() { ["Code"] = enrollmentCode }, ExtractToken(setupHtml));
        Check(setupPost.StatusCode == HttpStatusCode.Redirect && setupPost.Headers.Location?.OriginalString == "/admin", "TOTP enrollment grants full admin session");
        using (var scope = factory.Services.CreateScope())
            await WishPoolSchema.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        var wishHtml = WebUtility.HtmlDecode(await (await client.GetAsync("/admin/wishes")).Content.ReadAsStringAsync());
        Check(wishHtml.Contains("自动选择最优渲染方案") && wishHtml.Contains("（示例）"), "Admin can read seeded wishes");
        var wishFields = new Dictionary<string, string> { ["Id"] = "demo-wish-05", ["Status"] = "planned", ["ExpectedStatus"] = "pending", ["Reason"] = "test wish planning" };
        Check((await Post(client, "/admin/wishes", wishFields)).StatusCode == HttpStatusCode.BadRequest, "Wish mutations require CSRF");
        var wishUpdate = await Form(client, "/admin/wishes", null, wishFields);
        Check(wishUpdate.StatusCode == HttpStatusCode.Redirect, "Wish progress updated through authenticated form: " +
            WebUtility.HtmlDecode(Regex.Match(await wishUpdate.Content.ReadAsStringAsync(), "<div[^>]*validation-summary-errors[\\s\\S]*?</div>").Value));
        Check((await Form(client, "/admin/wishes", null, wishFields)).StatusCode == HttpStatusCode.OK, "Stale wish status cannot overwrite newer progress");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Check((await db.Wishes.SingleAsync(w => w.Id == "demo-wish-05")).Status == "planned", "Admin wish status persisted");
            Check(await db.AuditEntries.CountAsync(a => a.Action == "wish.status") == 1, "Wish status audited exactly once");
        }
        var adminCookie = setupPost.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-MuAdmin=", StringComparison.Ordinal)).Split(';')[0];
        Check(setupPost.Headers.GetValues("Set-Cookie").Any(value => value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)), "Admin cookie protected");
        foreach (var path in new[] { "/admin", "/admin/users", "/admin/telemetry", "/admin/features", "/admin/plans", "/admin/orders", "/admin/audit", "/admin/mail" })
            Check((await client.GetAsync(path)).IsSuccessStatusCode, "Authorized page renders: " + path);

        using (var scope = factory.Services.CreateScope())
        {
            await using var mailDb = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MailLogDbContext>>().CreateDbContextAsync();
            for (var i = 0; i < 55; i++) mailDb.MailLogs.Add(new() { Id = Guid.NewGuid().ToString("N"), Email = $"monitor{i}@example.test",
                Purpose = "register", Provider = "smtp", Status = "delivered", CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Detail = "<script>alert('test')</script>" });
            await mailDb.SaveChangesAsync();
        }
        var mailResponse = await client.GetAsync("/admin/mail?Search=monitor&Status=delivered&PageNumber=2");
        var mailHtml = await mailResponse.Content.ReadAsStringAsync();
        Check(mailResponse.IsSuccessStatusCode && mailResponse.Headers.CacheControl?.NoStore == true, "Mail logs are private and uncached");
        Check(Regex.Matches(mailHtml, "@example.test").Count == 5, "Mail log pagination limits results");
        Check(!mailHtml.Contains("<script>alert"), "SMTP results are HTML encoded");
        var filtered = await (await client.GetAsync("/admin/mail?Search=monitor&Status=bounced")).Content.ReadAsStringAsync();
        Check(!filtered.Contains("monitor0@example.test"), "Mail status filtering applied");
        var telemetryHtml = await (await client.GetAsync("/admin/telemetry")).Content.ReadAsStringAsync();
        Check(telemetryHtml.Contains("Cyberpunk &lt;test&gt;", StringComparison.Ordinal)
            && !telemetryHtml.Contains("Cyberpunk <test>", StringComparison.Ordinal), "Telemetry game names are HTML escaped");
        Check(telemetryHtml.Contains("RX 7900", StringComparison.Ordinal)
            && !telemetryHtml.Contains("Old game", StringComparison.Ordinal), "Default report includes recent records only");
        Check(telemetryHtml.Contains("有 FPS 的会话", StringComparison.Ordinal)
            && telemetryHtml.Contains("缺少 FPS 的会话", StringComparison.Ordinal), "Report distinguishes measured and unmeasured sessions");
        var allTelemetryHtml = await (await client.GetAsync("/admin/telemetry?Days=0")).Content.ReadAsStringAsync();
        Check(allTelemetryHtml.Contains("Old game", StringComparison.Ordinal), "All-time report includes older records");
        var filteredTelemetryHtml = await (await client.GetAsync("/admin/telemetry?Days=30&Game=Cyberpunk")).Content.ReadAsStringAsync();
        Check(filteredTelemetryHtml.Contains("Cyberpunk &lt;test&gt;", StringComparison.Ordinal)
            && !filteredTelemetryHtml.Contains("RE9", StringComparison.Ordinal), "Game filter applies to every report table");

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
        Check((await normal.GetAsync("/admin/telemetry")).StatusCode == HttpStatusCode.Redirect, "Client bearer cannot read telemetry report");
        Check((await normal.GetAsync("/admin/wishes")).StatusCode == HttpStatusCode.Redirect, "Client bearer cannot manage wishes");
        Check((await normal.GetFromJsonAsync<WishDetail>("/api/v1/wishes/demo-wish-05"))!.Wish.Status == "planned", "Admin progress update visible through client API");
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
