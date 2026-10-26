using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Presets;
using Mu.Server;
using Mu.Server.Data;

internal static class CommunitySharingHttpTests
{
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
        await using var factory = new AccountFactory();
        using var client = factory.CreateClient();
        Check((await client.GetAsync("/api/v1/presets")).StatusCode == HttpStatusCode.Unauthorized, "Global list needs login");
        Check((await client.GetAsync("/api/v1/presets/all")).StatusCode == HttpStatusCode.Unauthorized, "Explicit global list needs login");
        Check((await client.GetAsync("/api/v1/presets/missing/attachment")).StatusCode == HttpStatusCode.Unauthorized, "Attachment needs login");
        const string email = "community-sharing@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/code", new { email, purpose = "register" });
        var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, code = factory.Mail.Codes[email + ":register"], password = "correct horse battery", termsVersion = "2026-09-26", deviceName = "sharing test" });
        var session = (await registered.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var old = new PresetSubmission(Guid.NewGuid().ToString("N"), "steam:1", "游戏甲", 4, "performance", false, "fixture", "旧版游戏方案", "保留旧数据", null, [new("OptiScaler.ini", "XeFG", "InterpolationCount", "1")]);
        var first = await client.PostAsJsonAsync("/api/v1/presets", old);
        Check(first.IsSuccessStatusCode, "Old submission contract remains accepted: " + await first.Content.ReadAsStringAsync());
        var bytes = System.Text.Encoding.UTF8.GetBytes("[Quality]\nValue=1");
        var resource = old with { SubmissionId = Guid.NewGuid().ToString("N"), GameKey = "universal", GameName = "通用资源", Mode = -1, Goal = "custom", Settings = [], Scope = "universal", Title = "通用画质资源", ResourceUrl = "https://pan.quark.cn/s/test", ResourceCode = "abcd", Attachment = new("画质预设.ini", Convert.ToBase64String(bytes)) };
        var posted = await client.PostAsJsonAsync("/api/v1/presets", resource);
        Check(posted.IsSuccessStatusCode, "Resources-only upload failed: " + await posted.Content.ReadAsStringAsync());
        var saved = (await posted.Content.ReadFromJsonAsync<PresetDetail>())!;
        var list = (await client.GetFromJsonAsync<PresetList>("/api/v1/presets"))!;
        Check(list.Total == 2 && list.Items.Any(x => x.GameKey == "steam:1") && list.Items.Any(x => x.Scope == "universal"), "List was still bound to a game");
        Check(list.Items.Single(x => x.Id == saved.Item.Id).Attachment is { Size: > 0 } && !(await client.GetStringAsync("/api/v1/presets")).Contains(resource.Attachment.Data), "List leaked attachment body or lost metadata");
        var filtered = (await client.GetFromJsonAsync<PresetList>("/api/v1/presets?gameKey=steam:2"))!;
        Check(filtered.Total == 2, "Legacy game parameters must no longer filter out other games");
        var unbound = (await client.GetFromJsonAsync<PresetList>("/api/v1/presets/all?gameKey=not-a-game"))!;
        Check(unbound.Total == 2, "Browsing must not validate or require a game identifier");
        Check((await client.PostAsJsonAsync("/api/v1/presets", old with { SubmissionId = Guid.NewGuid().ToString("N"), GameKey = "invalid" })).StatusCode == HttpStatusCode.BadRequest, "Game-specific publishing still validates its actual source game");
        var downloaded = (await client.GetFromJsonAsync<PresetAttachmentDownload>($"/api/v1/presets/{saved.Item.Id}/attachment"))!;
        Check(Convert.FromBase64String(downloaded.Data).SequenceEqual(bytes) && downloaded.Attachment.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)), "Attachment did not round trip with SHA256");
        var repeat = await client.PostAsJsonAsync("/api/v1/presets", resource);
        Check((await repeat.Content.ReadFromJsonAsync<PresetDetail>())?.Item.Id == saved.Item.Id, "Upload retry duplicated content");
        var changed = await client.PostAsJsonAsync("/api/v1/presets", resource with { ResourceCode = "changed" });
        Check(changed.StatusCode == HttpStatusCode.Conflict, "Changed attachment/link reused submission ID");
        foreach (var invalid in new[] { resource with { ResourceUrl = "javascript:alert(1)" }, resource with { Attachment = new("../secret.ini", "YQ==") }, resource with { Attachment = new("install.exe", "YQ==") }, resource with { ResourceUrl = null, ResourceCode = null, Attachment = null } })
            Check((await client.PostAsJsonAsync("/api/v1/presets", invalid with { SubmissionId = Guid.NewGuid().ToString("N") })).StatusCode == HttpStatusCode.BadRequest, "Invalid resource was accepted");
        var largeBytes = new byte[1024 * 1024]; largeBytes[0] = 1;
        var large = await client.PostAsJsonAsync("/api/v1/presets", resource with { SubmissionId = Guid.NewGuid().ToString("N"), Title = "大附件上传测试", Attachment = new("资源包.zip", Convert.ToBase64String(largeBytes)) });
        Check(large.IsSuccessStatusCode, "Real upload exceeded old request limit: " + await large.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.CommunityPresetAttachments.SingleAsync(x => x.PresetId == saved.Item.Id); stored.Content[0] ^= 1; db.Entry(stored).Property(x => x.Content).IsModified = true; await db.SaveChangesAsync();
            var other = new ApplicationUser { Id = Guid.NewGuid().ToString("N"), UserName = "other-sharing", NormalizedUserName = "OTHER-SHARING", Email = "other-sharing@example.com", NormalizedEmail = "OTHER-SHARING@EXAMPLE.COM" };
            db.Users.Add(other);
            for (var i = 0; i < 31; i++) db.CommunityPresets.Add(new() { UserId = i % 2 == 0 ? other.Id : session.Account.Id, SubmissionId = Guid.NewGuid().ToString("N"), GameKey = "steam:99", GameName = "分页测试", Mode = 4, Goal = "performance", ComponentVersion = "fixture", Title = "分页测试" + i, Description = "fixture", Author = "fixture", CreatedAt = i });
            await db.SaveChangesAsync();
        }
        Check((await client.GetAsync($"/api/v1/presets/{saved.Item.Id}/attachment")).StatusCode == HttpStatusCode.Conflict, "Corrupted attachment was served");
        var page1 = (await client.GetFromJsonAsync<PresetList>("/api/v1/presets/all?page=1"))!;
        var page2 = (await client.GetFromJsonAsync<PresetList>("/api/v1/presets/all?page=2"))!;
        Check(page1.Total == 34 && page1.Items.Length == 30 && page2.Items.Length == 4 && !page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)).Any(), "Global pagination lost or repeated entries");
        Check(page1.Items.Concat(page2.Items).Any(x => !x.IsMine), "Global list excluded another author");
        await using (var legacyDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options))
        {
            await legacyDb.Database.OpenConnectionAsync();
            await legacyDb.Database.ExecuteSqlRawAsync("""
                CREATE TABLE CommunityPresets (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, SubmissionId TEXT NOT NULL,
                GameKey TEXT NOT NULL, GameName TEXT NOT NULL, Mode INTEGER NOT NULL, Goal TEXT NOT NULL, Vulkan INTEGER NOT NULL,
                ComponentVersion TEXT NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL, Author TEXT NOT NULL,
                CreatedAt INTEGER NOT NULL, SettingsJson TEXT NOT NULL, ImageData TEXT NULL);
                INSERT INTO CommunityPresets VALUES ('old','owner','submission','steam:1','旧游戏',4,'performance',0,'v1','旧方案','说明','作者',1,'[]',NULL);
                """);
            await CommunityPresetSchema.InitializeAsync(legacyDb);
            await CommunityPresetSchema.InitializeAsync(legacyDb);
            var preserved = await legacyDb.CommunityPresets.AsNoTracking().SingleAsync();
            Check(preserved is { Scope: "game", Title: "旧方案", ResourceUrl: null, AttachmentSize: 0 }, "Legacy schema migration changed old sharing data");
            Check(await legacyDb.CommunityPresetAttachments.CountAsync() == 0, "Legacy migration created fake attachments");
        }
        Console.WriteLine($"Community sharing HTTP: {count} assertions passed.");
    }
}
