using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Mu.Compatibility;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class CompatibilityTests
{
    public static async Task<int> RunAsync(IServiceProvider services, FakeClock clock, string userId)
    {
        var assertions = CompatibilityMetadataTests.Run();
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new Exception("FAILED compatibility: " + message);
        }
        async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider);
        }
        Task<T> Use<T>(Func<CompatibilityService, Task<T>> action) => InScope(p => action(p.GetRequiredService<CompatibilityService>()));
        Task<CompatibilityTest> Submit(CompatibilitySubmission input) => Use(c => c.SubmitAsync(userId, input));
        async Task Error(Func<Task> action, int status, string code)
        {
            try { await action(); }
            catch (ApiException ex) { Check(ex.StatusCode == status && ex.Error.Code == code, $"expected {status}/{code}, got {ex.StatusCode}/{ex.Error.Code}"); return; }
            throw new Exception("FAILED compatibility: expected " + code);
        }
        var initial = await Use(c => c.SearchAsync(userId, null, null));
        Check(initial.CatalogTotal >= 2000 && initial.Total == initial.CatalogTotal && initial.Items.Length == Math.Min(50, initial.CatalogTotal)
            && initial.Items.All(x => x.Status == "untested" && x.Counts.Total == 0 && x.LastTestedAt == null), "fresh catalog is entirely untested");
        Check(initial.Coverage is { Requirements: >= 4, ModCompatibility: >= 200 }
            && initial.Coverage.Requirements <= initial.CatalogTotal && initial.Coverage.ModCompatibility <= initial.CatalogTotal,
            "global coverage counts sourced games separately from the full catalog");
        await InScope(async p =>
        {
            var catalog = await p.GetRequiredService<AppDbContext>().CompatibilityGames.AsNoTracking().ToArrayAsync();
            Check(catalog.All(x => x.SteamAppId != null) && catalog.Select(x => x.SteamAppId).Distinct().Count() == catalog.Length,
                "reviewed catalog has unique Steam identities");
            Check(catalog.All(x => CompatibilityCatalog.FindInfo(x.SteamAppId) is { Provider: "Steam Store" } info
                && info.Url == $"https://store.steampowered.com/app/{x.SteamAppId}/" && info.RetrievedAt != default && info.ReleaseDate != null),
                "every imported game exposes an official source and retrieval metadata");
            Check(catalog.Count(x => CompatibilityMetadata.Requirements(x.SteamAppId)?.Status == "available") >= 4,
                "embedded release includes real published requirements for the priority games");
            Check(catalog.Count(x => CompatibilityMetadata.Adaptations(x.SteamAppId).Length > 0) >= 200,
                "embedded upstream adaptation facts cover reviewed Steam identities");
            Check(initial.Coverage == new CompatibilityCoverage(catalog.Count(x => CompatibilityMetadata.Requirements(x.SteamAppId)?.Status == "available"),
                catalog.Count(x => CompatibilityMetadata.Adaptations(x.SteamAppId).Length > 0)), "coverage exactly reflects available requirements and matched game identities");
            Check(catalog.SelectMany(x => CompatibilityMetadata.Adaptations(x.SteamAppId)).All(x => !x.MuVerified),
                "upstream adaptation evidence never claims MU verification");
            var identities = new Dictionary<string, string>
            {
                ["1091500"] = "cyberpunk-2077", ["271590"] = "gta-v-legacy", ["3240220"] = "gta-v-enhanced", ["2358720"] = "black-myth-wukong"
            };
            Check(identities.All(x => catalog.Any(game => game.SteamAppId == x.Key && game.Id == x.Value)), "original four stable game IDs survive catalog expansion");
            return true;
        });
        Check((await Use(c => c.SearchAsync(userId, "gta v", null))).Items.Select(x => x.Game.SteamAppId).Order().SequenceEqual(new[] { "271590", "3240220" }), "GTA search distinguishes editions");
        Check((await Use(c => c.SearchAsync(userId, "黑神话", null))).Items.Single().Game.SteamAppId == "2358720", "Chinese game alias searchable");
        var sourcedSummary = (await Use(c => c.SearchAsync(userId, "1091500", "unknown GPU"))).Items.Single();
        var sourcedDetail = await Use(c => c.DetailAsync(userId, "cyberpunk-2077", "unknown GPU"));
        Check(sourcedSummary.Evidence is { HasRequirements: true, ModStatus: "working" }
            && sourcedSummary.Status == "untested" && sourcedSummary.Counts.Total == 0,
            "source evidence stays separate from reports even under an unknown GPU filter");
        Check((await Use(c => c.SearchAsync(userId, "1091500", "unknown GPU"))).Coverage == initial.Coverage
            && (await Use(c => c.SearchAsync(userId, "no-such-game-for-coverage", null, 2, 1))).Coverage == initial.Coverage,
            "query, GPU filter and empty page never change global source coverage");
        Check(sourcedDetail.Requirements is { Status: "available", Minimum.Graphics: not null }
            && sourcedDetail.ModCompatibility is { Length: > 0 } && sourcedDetail.Tests.Length == 0,
            "detail includes requirements and upstream facts without generating tests");
        var summaryJson = JsonSerializer.Serialize(sourcedSummary);
        Check(!summaryJson.Contains("Minimum", StringComparison.Ordinal) && !summaryJson.Contains("ModCompatibility", StringComparison.Ordinal),
            "paginated summaries do not duplicate full requirements or adaptation payloads");
        var env = new CompatibilityEnvironment(new("AMD Radeon RX 7900 XT", "AMD", 20480, "RDNA 3", "26.8.1"),
            "Windows 11", "12", "2.3", "DX12", "unknown", "unknown", "2.0.0", "1440p", "unknown");
        CompatibilitySubmission Input(CompatibilityEnvironment environment, string result = "success") => new(Guid.NewGuid().ToString("N"), "cyberpunk-2077", "Cyberpunk 2077", "1091500", environment, result, null, null);
        var input = Input(env);
        var first = await Submit(input);
        Check(first.Environment == env && first.Result == "success" && first.FailureReason == null, "complete environment retained");
        Check(first.Tester.StartsWith("tester-") && !first.Tester.Contains('@') && first.Tester != userId, "tester identity anonymous");
        Check((await Submit(input)).Id == first.Id, "identical replay idempotent");
        await Error(() => Submit(input with { Result = "failure" }), 409, "submission_conflict");
        await Error(() => Submit(Input(env, "failure")), 409, "environment_already_reported");
        Check((await Use(c => c.DetailAsync(userId, first.GameId, null))).Counts.Total == 1, "replays never inflate totals");
        var partial = await Submit(Input(env with { Gpu = env.Gpu with { DriverVersion = "26.8.2" } }, "partial"));
        var failure = await Submit(Input(env with { Gpu = env.Gpu with { Name = "AMD Radeon RX 7800 XT" } }, "failure"));
        Check(partial.FailureReason == "unknown" && partial.Tester == first.Tester && failure.Tester == first.Tester, "unknown reason normalized and tester stable");
        var all = await Use(c => c.DetailAsync(userId, first.GameId, null));
        Check(all.Counts == new CompatibilityCounts(1, 1, 1) && all.Status == "mixed" && all.Gpus.Length == 2, "aggregate retains mixed outcome");
        var filtered = await Use(c => c.DetailAsync(userId, first.GameId, "amd radeon rx 7800 xt"));
        Check(filtered.Status == "failure" && filtered.Counts == new CompatibilityCounts(0, 0, 1) && filtered.Tests.Single().Id == failure.Id && filtered.Gpus.Length == 2, "GPU detail filters totals and reports while preserving all GPU overview");
        var missing = await Use(c => c.DetailAsync(userId, first.GameId, "RX 9070 XT"));
        Check(missing.Status == "untested" && missing.Tests.Length == 0 && missing.LastTestedAt == null, "untested GPU does not inherit other hardware results");
        var search = await Use(c => c.SearchAsync(userId, "1091500", env.Gpu.Name));
        Check(search.Items.Single().Counts == new CompatibilityCounts(1, 1, 0) && search.Items.Single().Status == "mixed", "search uses exact GPU filter");
        await Error(() => Submit(Input(env) with { GameId = "gta-v-legacy" }), 409, "game_identity_mismatch");
        await Error(() => Submit(Input(env) with { SteamAppId = null, GameName = "Other game" }), 409, "game_identity_mismatch");
        await Error(() => Submit(Input(env) with { GameId = "missing" }), 404, "game_not_found");
        await Error(() => Submit(Input(env) with { Environment = null! }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { Environment = env with { Gpu = null! } }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { GameName = new string('x', 201) }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { Result = "untested" }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { FailureReason = "startup_crash" }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { Result = "failure", FailureReason = "invented" }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { SteamAppId = "-1" }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { Environment = env with { RenderApi = "unsupported" } }), 400, "invalid_compatibility_submission");
        await Error(() => Submit(Input(env) with { SubmissionId = "" }), 400, "invalid_compatibility_submission");
        var newSteam = await Submit(Input(env) with { GameId = null, SteamAppId = "999000", GameName = "Steam test game" });
        var reusedSteam = await Submit(Input(env with { GameVersion = "next" }) with { GameId = null, SteamAppId = "999000", GameName = "Localized Steam name" });
        Check(newSteam.GameId == reusedSteam.GameId, "Steam identity does not create duplicate games for translated names");
        var local = await Submit(Input(env) with { GameId = null, SteamAppId = null, GameName = "Non Steam game" });
        var localAgain = await Submit(Input(env with { GameVersion = "next" }) with { GameId = null, SteamAppId = null, GameName = "non steam game" });
        Check(local.GameId == localAgain.GameId, "local games reuse normalized names");
        var parallelInput = Input(env with { GameVersion = "concurrency" });
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Submit(parallelInput))));
        Check(concurrent.Select(x => x.Id).Distinct().Count() == 1, "simultaneous identical submissions create exactly one report");
        var duplicateInput = Input(env with { GameVersion = "concurrent-environment" });
        async Task<bool> RacingEnvironment()
        {
            try { await Submit(duplicateInput with { SubmissionId = Guid.NewGuid().ToString("N") }); return true; }
            catch (ApiException ex) when (ex.Error.Code == "environment_already_reported") { return false; }
        }
        Check((await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(RacingEnvironment)))).Count(x => x) == 1, "concurrent different ids cannot inflate same-environment totals");
        var countsBeforeSeed = (await Use(c => c.DetailAsync(userId, first.GameId, null))).Counts;
        await InScope(async p =>
        {
            var db = p.GetRequiredService<AppDbContext>();
            var idsBefore = await db.CompatibilityGames.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var reportsBefore = JsonSerializer.Serialize(await db.CompatibilityTests.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
            var game = await db.CompatibilityGames.SingleAsync(x => x.Id == first.GameId);
            var officialName = game.Name;
            game.Name = "Stale catalog title";
            game.NormalizedName = "STALE CATALOG TITLE";
            await db.SaveChangesAsync();
            await CompatibilityCatalog.SeedAsync(db);
            await db.SaveChangesAsync();
            await CompatibilityCatalog.SeedAsync(db);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var refreshed = await db.CompatibilityGames.SingleAsync(x => x.Id == first.GameId);
            Check(refreshed.Name == officialName && refreshed.NormalizedName == officialName.ToUpperInvariant() && refreshed.SteamAppId == "1091500",
                "official metadata refresh repairs a stale name without changing the game ID");
            var idsAfter = await db.CompatibilityGames.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            Check(idsBefore.SequenceEqual(idsAfter),
                "reimport neither duplicates official entries nor deletes user-created games");
            Check(reportsBefore == JsonSerializer.Serialize(await db.CompatibilityTests.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()),
                "all reports and their game associations survive repeated metadata import byte-for-byte");
            Check((await db.CompatibilityTests.SingleAsync(x => x.Id == first.Id)).GameId == first.GameId, "existing report retains its original game foreign key");
            return true;
        });
        Check((await Use(c => c.DetailAsync(userId, first.GameId, null))).Counts == countsBeforeSeed, "catalog refresh cannot inflate or reset test counts");
        async Task Policy(string key, bool enabled, string tier)
        {
            await InScope(async p => { await p.GetRequiredService<AccountManagementService>().SetFeatureAsync(key, enabled, tier, "compatibility test", "test-admin"); return true; });
        }
        await Policy("compatibility.read", false, "standard");
        await Error(() => Use(c => c.SearchAsync(userId, null, null)), 403, "feature_disabled");
        await Error(() => Use(c => c.DetailAsync(userId, first.GameId, null)), 403, "feature_disabled");
        await Policy("compatibility.read", true, "standard");
        await Policy("compatibility.submit", true, "pro");
        await Error(() => Submit(input), 403, "pro_required");
        await Policy("compatibility.submit", false, "standard");
        await Error(() => Submit(input), 403, "feature_disabled");
        await Policy("compatibility.submit", true, "standard");

        clock.Advance(86400);
        Check((await Submit(Input(env))).Id != first.Id, "new UTC day permits retest");
        // Use a separate test account to make quota and pagination boundaries independent of earlier checks.
        const string quotaUser = "compatibility-quota-user";
        await InScope(async p => { var db = p.GetRequiredService<AppDbContext>(); db.Users.Add(new() { Id = quotaUser, Email = "quota@example.test" }); await db.SaveChangesAsync(); return true; });
        async Task<CompatibilityTest> Quota(int number) => await Use(c => c.SubmitAsync(quotaUser,
            new(Guid.NewGuid().ToString("N"), "black-myth-wukong", "Black Myth: Wukong", "2358720", env with { GameVersion = "quota-" + number }, "partial", "performance", null)));
        CompatibilityTest? last = null;
        for (var i = 0; i < 100; i++)
        {
            if (i != 0 && i % 20 == 0) clock.Advance(3601);
            last = await Quota(i);
            if (i == 19) await Error(() => Quota(1000), 429, "compatibility_rate_limited");
        }
        clock.Advance(3601);
        await Error(() => Quota(1001), 429, "compatibility_rate_limited");
        var history = await Use(c => c.DetailAsync(userId, "black-myth-wukong", null));
        Check(history.Counts.Total == 100 && history.Counts.Partial == 100 && history.Status == "partial" && history.Tests.Length == 50, "counts cover all reports while history is capped at fifty");
        Check(history.Tests.Any(x => x.Id == last!.Id) && history.LastTestedAt == last!.CreatedAt, "recent history and server timestamps retained");
        await InScope(async p =>
        {
            var db = p.GetRequiredService<AppDbContext>();
            var stored = await db.CompatibilityTests.SingleAsync(x => x.Id == first.Id);
            Check(stored.UserId == userId, "real account foreign key retained for moderation");
            return true;
        });
        Console.WriteLine($"PASS: {assertions} compatibility service assertions (real SQLite, concurrent deduplication, quotas, policies and aggregation).");
        return assertions;
    }
}
