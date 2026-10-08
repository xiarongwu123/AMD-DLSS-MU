using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server;
using Mu.Server.Data;
using Mu.Wishes;

internal static class WishHttpTests
{
    public static async Task<int> RunAsync()
    {
        using var factory = new AccountFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException("Wishes HTTP: " + message); }
        async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Check(response.StatusCode == status, $"expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Check((await response.Content.ReadFromJsonAsync<ApiError>())?.Code == code, "stable error code " + code);
        }
        async Task<HttpClient> Login(string email)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.CreateAsync(new() { Email = email, UserName = email, EmailConfirmed = true }, "wish-test-password");
            Check(result.Succeeded, "fixture user created");
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "wish-test-password", "Wish test"));
            Check(response.IsSuccessStatusCode, "fixture login");
            var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            return client;
        }
        const string jpeg = "data:image/jpeg;base64,/9j/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAgP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCawFgf/9k=";
        var submission = new WishSubmission(Guid.NewGuid().ToString("N"), "希望增加独立的游戏配置方案。", jpeg);
        Check((await anonymous.GetAsync("/api/v1/wishes")).StatusCode == HttpStatusCode.Unauthorized, "list needs login");
        Check((await anonymous.PostAsJsonAsync("/api/v1/wishes", submission)).StatusCode == HttpStatusCode.Unauthorized, "submit needs login");
        Check((await anonymous.PutAsJsonAsync("/api/v1/wishes/missing/vote", new WishVoteRequest(true))).StatusCode == HttpStatusCode.Unauthorized, "vote needs login");
        using var client = await Login("wish-one@example.test");
        using var other = await Login("wish-two@example.test");
        var initial = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes"))!;
        Check(initial.Total == 0, "no automatic fake data");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await WishPoolSchema.SeedAsync(db); await WishPoolSchema.SeedAsync(db);
            Check(await db.Wishes.CountAsync() == 8, "seed is idempotent and persisted");
            // Re-running startup on an existing DB must preserve the account version and module rows.
            await DatabaseSetup.InitializeAsync(db);
            Check(await db.Wishes.CountAsync() == 8 && (await db.DatabaseSchemas.SingleAsync()).Version == DatabaseSetup.CurrentVersion, "existing DB initialization preserves rows");
        }
        var seeded = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes"))!;
        Check(seeded.Items.Length == 8 && seeded.Items.All(w => w.IsDemo && !w.IsMine), "demo data labelled and not owned by real users");
        Check(seeded.Counts == new WishCounts(8, 8, 2, 2, 1, 0), "filter counts derived from DB");
        var developing = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?filter=developing"))!;
        Check(developing.Total == 2 && developing.Items.All(w => w.Status == "developing"), "status filter");
        var search = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?q=" + Uri.EscapeDataString("快捷键")))!;
        Check(search.Total == 1 && search.Items[0].Status == "completed", "text search");
        var sorted = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?sort=votes"))!;
        Check(sorted.Items[0].Votes == 256 && sorted.Items[^1].Votes == 53, "vote sort");
        await Error(await client.GetAsync("/api/v1/wishes?filter=unknown"), HttpStatusCode.BadRequest, "invalid_wish_query");
        await Error(await client.GetAsync("/api/v1/wishes?page=0"), HttpStatusCode.BadRequest, "invalid_wish_query");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", submission with { Content = "短" }), HttpStatusCode.BadRequest, "invalid_wish");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", submission with { Content = new string('a', 501) }), HttpStatusCode.BadRequest, "invalid_wish");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", submission with { ImageData = "data:image/svg+xml;base64,PHN2Zy8+" }), HttpStatusCode.BadRequest, "invalid_wish_image");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", submission with { ImageData = "data:image/jpeg;base64,/9j/2Q==" }), HttpStatusCode.BadRequest, "invalid_wish_image");
        var creations = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/v1/wishes", submission)));
        Check(creations.All(r => r.IsSuccessStatusCode), "concurrent submission retries all succeed");
        var createdDetails = await Task.WhenAll(creations.Select(async r => (await r.Content.ReadFromJsonAsync<WishDetail>())!));
        Check(createdDetails.Select(w => w.Wish.Id).Distinct().Count() == 1, "concurrent submissions create one row");
        var created = createdDetails[0];
        Check(created.ImageData == jpeg && created.Wish.HasImage, "JPEG attachment survives concurrent submission");
        Check(created.Wish.IsMine && !created.Wish.IsDemo && created.Wish.Status == "pending" && created.Wish.Votes == 0, "real submission independent of demos");
        Check(!created.Wish.Author.Contains("@") && !created.Wish.Author.Contains("wish-one"), "author never exposes email");
        var replay = await client.PostAsJsonAsync("/api/v1/wishes", submission);
        Check((await replay.Content.ReadFromJsonAsync<WishDetail>())!.Wish.Id == created.Wish.Id, "submission retry is idempotent");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", submission with { Content = "希望增加另一种完全不同的功能。" }), HttpStatusCode.Conflict, "wish_conflict");
        var mine = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?filter=mine"))!;
        Check(mine.Total == 1 && mine.Items.Single().Id == created.Wish.Id, "mine scope");
        Check((await other.GetFromJsonAsync<WishList>("/api/v1/wishes?filter=mine"))!.Total == 0, "mine isolated between accounts");
        var voteUrl = "/api/v1/wishes/" + created.Wish.Id + "/vote";
        var vote = (await (await client.PutAsJsonAsync(voteUrl, new WishVoteRequest(true))).Content.ReadFromJsonAsync<WishItem>())!;
        Check(vote.Votes == 1 && vote.Voted, "vote saved");
        var voteReplay = (await (await client.PutAsJsonAsync(voteUrl, new WishVoteRequest(true))).Content.ReadFromJsonAsync<WishItem>())!;
        Check(voteReplay.Votes == 1, "repeat vote cannot double count");
        var concurrentVotes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PutAsJsonAsync(voteUrl, new WishVoteRequest(true))));
        Check(concurrentVotes.All(r => r.IsSuccessStatusCode), "concurrent votes are idempotent");
        var otherVote = (await (await other.PutAsJsonAsync(voteUrl, new WishVoteRequest(true))).Content.ReadFromJsonAsync<WishItem>())!;
        Check(otherVote.Votes == 2 && !otherVote.IsMine, "different users count independently");
        var removed = (await (await client.PutAsJsonAsync(voteUrl, new WishVoteRequest(false))).Content.ReadFromJsonAsync<WishItem>())!;
        Check(removed.Votes == 1 && !removed.Voted, "unvote preserves other user's vote");
        Check((await (await client.PutAsJsonAsync(voteUrl, new WishVoteRequest(false))).Content.ReadFromJsonAsync<WishItem>())!.Votes == 1, "repeat unvote is idempotent");
        var commentUrl = "/api/v1/wishes/" + created.Wish.Id + "/comments";
        await Error(await client.PostAsJsonAsync(commentUrl, new WishCommentRequest(" ")), HttpStatusCode.BadRequest, "invalid_wish_comment");
        Check((await other.PostAsJsonAsync(commentUrl, new WishCommentRequest("同样期待这个功能！"))).IsSuccessStatusCode, "comment accepted");
        var detail = (await client.GetFromJsonAsync<WishDetail>("/api/v1/wishes/" + created.Wish.Id))!;
        Check(detail.Wish.Comments == 1 && detail.Comments.Single().Content == "同样期待这个功能！", "comment and count persisted");
        await Error(await client.GetAsync("/api/v1/wishes/missing"), HttpStatusCode.NotFound, "wish_not_found");
        // A real 1x1 PNG verifies the image survives storage and is absent from list responses.
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZuoAAAAASUVORK5CYII=";
        var attached = await client.PostAsJsonAsync("/api/v1/wishes", new WishSubmission(Guid.NewGuid().ToString("N"), "希望附图说明界面的改进建议。", png));
        var attachedDetail = (await attached.Content.ReadFromJsonAsync<WishDetail>())!;
        Check(attachedDetail.ImageData == png && attachedDetail.Wish.HasImage, "image persisted");
        var listJson = await client.GetStringAsync("/api/v1/wishes");
        Check(!listJson.Contains("base64") && !listJson.Contains("example.test"), "list excludes image bodies and emails");
        for (var i = 0; i < 8; i++)
            Check((await client.PostAsJsonAsync("/api/v1/wishes", new WishSubmission(Guid.NewGuid().ToString("N"), "测试每日愿望上限，第 " + i + " 条。"))).IsSuccessStatusCode, "within daily limit");
        await Error(await client.PostAsJsonAsync("/api/v1/wishes", new WishSubmission(Guid.NewGuid().ToString("N"), "这条超出每日上限。")), HttpStatusCode.TooManyRequests, "wish_rate_limited");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 12; i++) db.Wishes.Add(new() { Id = "pagination-" + i, SubmissionId = "pagination-" + i, IsDemo = true, Content = "pagination", Title = "pagination", Author = "demo", CreatedAt = 1 });
            await db.SaveChangesAsync();
        }
        var page1 = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?page=1"))!;
        var page2 = (await client.GetFromJsonAsync<WishList>("/api/v1/wishes?page=2"))!;
        Check(page1.Items.Length == 20 && page2.Items.Length == 10 && page1.Total == 30 && !page1.Items.Select(w => w.Id).Intersect(page2.Items.Select(w => w.Id)).Any(), "pagination stable without overlap");
        Console.WriteLine($"Wishes HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
