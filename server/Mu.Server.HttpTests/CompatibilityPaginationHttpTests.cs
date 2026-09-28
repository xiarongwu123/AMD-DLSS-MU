using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Compatibility;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class CompatibilityPaginationHttpTests
{
    private sealed record LegacySearchResponse(CompatibilitySummary[] Items);

    public static async Task<int> RunAsync()
    {
        using var factory = new AccountFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Compatibility pagination HTTP: " + message);
        }
        const string userId = "pagination-test-user";
        const string token = "pagination-access-token-for-http-test-0001";
        const int fixtures = 1006;
        int catalogTotal;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new() { Id = userId, Email = "pagination@example.test" });
            db.AuthSessions.Add(new()
            {
                UserId = userId, FamilyId = "pagination-test", AccessHash = AccountService.HashToken(token),
                RefreshHash = AccountService.HashToken("pagination-refresh-token"),
                AccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(), RefreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
            });
            // Test fixtures deliberately share names to exercise the secondary ID ordering.
            for (var i = fixtures - 1; i >= 0; i--)
            {
                var name = $"Pagination fixture {i / 2:D4}";
                var steamId = (900000000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
                db.CompatibilityGames.Add(new()
                {
                    Id = $"pagination-{i:D4}", Name = name, NormalizedName = name.ToUpperInvariant(), SteamAppId = steamId,
                    SearchText = (name + " 分页测试目录 " + steamId).ToUpperInvariant()
                });
            }
            await db.SaveChangesAsync();
            catalogTotal = await db.CompatibilityGames.CountAsync();
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var sourcedGame = (await anonymous.GetFromJsonAsync<CompatibilitySearchResponse>("/api/v1/compatibility/public/games?q=1091500"))!.Items.Single();
        Check(sourcedGame.Game.Catalog is { Provider: "Steam Store", Url: "https://store.steampowered.com/app/1091500/" } source
            && source.RetrievedAt != default && source.ReleaseDate != null, "catalog source metadata survives public DTO serialization");
        Check(sourcedGame.Status == "untested" && sourcedGame.Counts.Total == 0, "catalog and technical reference provenance never imply real test outcomes");
        async Task<CompatibilitySearchResponse> Read(HttpClient connection, string route)
        {
            var response = await connection.GetAsync(route);
            Check(response.IsSuccessStatusCode, "paged search succeeded: " + route);
            return (await response.Content.ReadFromJsonAsync<CompatibilitySearchResponse>())!;
        }
        foreach (var (connection, route) in new[]
        {
            (anonymous, "/api/v1/compatibility/public/games"),
            (client, "/api/v1/compatibility/games")
        })
        {
            var first = await Read(connection, route + "?q=pagination%20fixture");
            Check(first.Page == 1 && first.PageSize == 50 && first.Total == fixtures && first.CatalogTotal == catalogTotal && first.Items.Length == 50,
                "default pagination separates matched and full catalog totals");
            Check(first.Items.Select(x => x.Game.Id).SequenceEqual(Enumerable.Range(0, 50).Select(i => $"pagination-{i:D4}")), "duplicate names ordered by stable ID");
            var second = await Read(connection, route + "?q=pagination%20fixture&page=2&pageSize=50");
            Check(second.Items.Select(x => x.Game.Id).SequenceEqual(Enumerable.Range(50, 50).Select(i => $"pagination-{i:D4}")), "second page has expected IDs");
            Check(!first.Items.Select(x => x.Game.Id).Intersect(second.Items.Select(x => x.Game.Id)).Any(), "adjacent pages have no duplicates");
            var repeated = await Read(connection, route + "?q=pagination%20fixture&page=2&pageSize=50");
            Check(repeated.Items.Select(x => x.Game.Id).SequenceEqual(second.Items.Select(x => x.Game.Id)), "repeated pages remain stable");
            var boundary = await Read(connection, route + "?q=pagination%20fixture&page=2&pageSize=1");
            Check(boundary.Items.Single().Game.Id == "pagination-0001" && boundary.PageSize == 1, "same-name boundary does not lose or repeat rows");
            var last = await Read(connection, route + "?q=pagination%20fixture&page=21&pageSize=50");
            Check(last.Items.Length == 6 && last.Items[0].Game.Id == "pagination-1000" && last.Items[^1].Game.Id == "pagination-1005", "last page returns remaining records");
            var beyond = await Read(connection, route + "?q=pagination%20fixture&page=22");
            Check(beyond.Items.Length == 0 && beyond.Total == fixtures && beyond.CatalogTotal == catalogTotal, "valid page beyond data is empty without losing totals");
            var gpu = await Read(connection, route + "?q=pagination%20fixture&gpu=RX%207900%20XT&page=2");
            Check(gpu.Items.Length == 50 && gpu.Total == fixtures && gpu.CatalogTotal == catalogTotal
                && gpu.Items.All(x => x.Counts.Total == 0 && x.Status == "untested"), "GPU filter preserves untested games and catalog totals");
            var chinese = await Read(connection, route + "?q=" + Uri.EscapeDataString("分页测试目录") + "&page=21");
            Check(chinese.Total == fixtures && chinese.Items.Length == 6, "Chinese search spans large catalog");
            var steam = await Read(connection, route + "?q=900001005");
            Check(steam.Total == 1 && steam.Items.Single().Game.Id == "pagination-1005" && steam.CatalogTotal == catalogTotal, "Steam ID finds a game beyond first thousand entries");
            var none = await Read(connection, route + "?q=nonexistent-pagination-query");
            Check(none.Total == 0 && none.Items.Length == 0 && none.CatalogTotal == catalogTotal, "no matches still expose full catalog coverage");
            foreach (var invalid in new[] { "page=0", "page=-1", "pageSize=0", "pageSize=51", "pageSize=-1", "page=2147483647&pageSize=50" })
            {
                var error = await connection.GetAsync(route + "?" + invalid);
                Check(error.StatusCode == HttpStatusCode.BadRequest && (await error.Content.ReadFromJsonAsync<ApiError>())?.Code == "invalid_pagination", "invalid pagination rejected: " + invalid);
            }
            Check((await connection.GetAsync(route + "?page=abc")).StatusCode == HttpStatusCode.BadRequest, "non-integer page rejected by binding");
            Check((await connection.GetAsync(route + "?pageSize=2.5")).StatusCode == HttpStatusCode.BadRequest, "non-integer page size rejected by binding");
            var defaultResponse = await connection.GetAsync(route);
            var json = await defaultResponse.Content.ReadAsStringAsync();
            var legacy = JsonSerializer.Deserialize<LegacySearchResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(legacy?.Items.Length == 50, "legacy clients can ignore additive pagination metadata");
        }
        Check((await anonymous.GetAsync("/api/v1/compatibility/games?page=2")).StatusCode == HttpStatusCode.Unauthorized, "pagination does not bypass Client authorization");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Check(!await db.CompatibilityTests.AnyAsync(), "browsing a large catalog does not fabricate reports");
            var service = scope.ServiceProvider.GetRequiredService<CompatibilityService>();
            var oldPublicCall = await service.PublicSearchAsync("pagination fixture", null);
            var oldPrivateCall = await service.SearchAsync(userId, "pagination fixture", null);
            Check(oldPublicCall.Page == 1 && oldPublicCall.PageSize == 50 && oldPrivateCall.Page == 1 && oldPrivateCall.PageSize == 50,
                "existing service call signatures retain default pagination");
        }
        Console.WriteLine($"Compatibility pagination HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
