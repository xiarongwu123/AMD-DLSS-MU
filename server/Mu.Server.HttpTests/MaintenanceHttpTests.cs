using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Compatibility;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class MaintenanceHttpTests
{
    public static async Task<int> RunAsync()
    {
        using var factory = new AccountFactory(maintenanceEnabled: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Maintenance HTTP: " + message);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new() { Id = "maintenance-user", Email = "maintenance@example.test", PasswordHash = "preserve-password", SecurityStamp = "preserve-stamp" });
            db.AuthSessions.Add(new()
            {
                UserId = "maintenance-user", FamilyId = "maintenance-family", AccessHash = AccountService.HashToken("maintenance-access-token-for-http-test-0001"),
                RefreshHash = AccountService.HashToken("maintenance-refresh-token-for-http-test-0001"), AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
                RefreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
            });
            await db.SaveChangesAsync();
        }
        async Task<string> DatabaseDigest()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.OpenConnectionAsync();
            var connection = db.Database.GetDbConnection();
            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            var tables = new List<string>();
            await using (var reader = await tableCommand.ExecuteReaderAsync())
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            var digestInput = new StringBuilder();
            foreach (var table in tables)
            {
                await using var rowsCommand = connection.CreateCommand();
                rowsCommand.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";";
                await using var reader = await rowsCommand.ExecuteReaderAsync();
                var rows = new List<string>();
                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Add(JsonSerializer.Serialize(values));
                }
                digestInput.Append(table).Append(':').AppendJoin('\n', rows.Order(StringComparer.Ordinal)).Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput.ToString())));
        }
        var before = await DatabaseDigest();
        foreach (var route in new[] { "/health", "/api/health" })
            Check((await client.GetAsync(route)).IsSuccessStatusCode, "maintenance permits " + route);
        var search = (await client.GetFromJsonAsync<CompatibilitySearchResponse>("/api/v1/compatibility/public/games?q=GTA%20V"))!;
        Check(search.Items.Length == 2 && search.Items.All(x => x.Status == "untested"), "maintenance permits public catalog verification");
        var detail = (await client.GetFromJsonAsync<CompatibilityDetail>("/api/v1/compatibility/public/games/cyberpunk-2077"))!;
        Check(detail.Counts.Total == 0 && detail.Tests.Length == 0, "maintenance permits public detail verification");
        Check((await client.GetAsync("/api/v1/compatibility/public/gpus")).IsSuccessStatusCode, "maintenance permits public GPU verification");
        var input = new { email = "maintenance@example.test", password = "ignored", refreshToken = "maintenance-refresh-token-for-http-test-0001", purpose = "register" };
        var blocked = new List<(HttpMethod Method, string Route)>
        {
            (HttpMethod.Get, "/"), (HttpMethod.Get, "/admin"), (HttpMethod.Get, "/admin/login"),
            (HttpMethod.Post, "/admin/login"), (HttpMethod.Post, "/admin/features"),
            (HttpMethod.Post, "/api/v1/auth/code"), (HttpMethod.Post, "/api/v1/auth/register"),
            (HttpMethod.Post, "/api/v1/auth/login"), (HttpMethod.Post, "/api/v1/auth/refresh"),
            (HttpMethod.Post, "/api/v1/auth/reset-password"), (HttpMethod.Post, "/api/v1/auth/logout"),
            (HttpMethod.Post, "/api/v1/account/password"), (HttpMethod.Post, "/api/v1/account/authorize"),
            (HttpMethod.Get, "/api/v1/account/me"), (HttpMethod.Get, "/api/v1/compatibility/games"),
            (HttpMethod.Post, "/api/v1/compatibility/tests"), (HttpMethod.Post, "/api/v1/orders"),
            (HttpMethod.Post, "/api/v1/compatibility/public/games"), (HttpMethod.Head, "/api/v1/compatibility/public/gpus"),
            (HttpMethod.Get, "/api/v1/compatibility/public-other/games")
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", "maintenance-access-token-for-http-test-0001");
        client.DefaultRequestHeaders.Add("X-Maintenance-Bypass", "true");
        client.DefaultRequestHeaders.Add("X-Maintenance-Enabled", "false");
        foreach (var (method, route) in blocked)
        {
            using var request = new HttpRequestMessage(method, route);
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(input);
            var response = await client.SendAsync(request);
            Check(response.StatusCode == HttpStatusCode.ServiceUnavailable, "maintenance blocks " + method + " " + route);
            Check(response.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(30) && response.Headers.CacheControl?.NoStore == true, "maintenance retry/cache contract");
            if (method != HttpMethod.Head)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>();
                Check(error?.Code == "service_maintenance" && error.RetryAfterSeconds == 30, "structured maintenance error precedes auth and parsing");
            }
        }
        Check(before == await DatabaseDigest(), "maintenance health/public/blocked requests preserve every database row");
        Check(factory.Mail.Codes.IsEmpty, "maintenance never sends verification email");
        using var normalFactory = new AccountFactory();
        using var normal = normalFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Check((await normal.GetAsync("/api/v1/account/me")).StatusCode == HttpStatusCode.Unauthorized, "normal default uses account authentication rather than maintenance");
        Check((await normal.GetAsync("/admin/login")).IsSuccessStatusCode, "default normal mode serves admin login");
        Console.WriteLine($"Maintenance HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
