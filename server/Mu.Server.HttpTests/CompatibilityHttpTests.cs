using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Mu.Compatibility;
using Mu.Server;
using Mu.Server.Services;

internal static class CompatibilityHttpTests
{
    public static async Task<int> RunAsync(AccountFactory factory, HttpClient client)
    {
        var assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Compatibility HTTP: " + message);
        }
        async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Check(response.StatusCode == status, $"expected {status}, got {response.StatusCode}");
            Check((await response.Content.ReadFromJsonAsync<ApiError>())?.Code == code, "stable error code " + code);
        }
        var environment = new CompatibilityEnvironment(new("RX 7900 XT", "AMD", 20480, "RDNA 3", "26.8.1"),
            "Windows 11", "12", "2.3", "DX12", "unknown", "unknown", "2.0.0", "1440p", "none");
        var input = new CompatibilitySubmission(Guid.NewGuid().ToString("N"), "cyberpunk-2077", "Cyberpunk 2077", "1091500",
            environment, "success", null, "Manual in-game observation");
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Check((await anonymous.GetAsync("/api/v1/compatibility/games")).StatusCode == HttpStatusCode.Unauthorized, "search requires Client auth");
        Check((await anonymous.GetAsync("/api/v1/compatibility/games/cyberpunk-2077")).StatusCode == HttpStatusCode.Unauthorized, "detail requires Client auth");
        Check((await anonymous.PostAsJsonAsync("/api/v1/compatibility/tests", input)).StatusCode == HttpStatusCode.Unauthorized, "submit requires Client auth");
        var empty = (await client.GetFromJsonAsync<CompatibilitySearchResponse>("/api/v1/compatibility/games"))!;
        Check(empty.CatalogTotal >= 4 && empty.Total == empty.CatalogTotal && empty.Items.Length == Math.Min(50, empty.CatalogTotal)
            && empty.Items.All(x => x.Status == "untested" && x.Counts.Total == 0), "directory has no fabricated reports");
        var gta = (await client.GetFromJsonAsync<CompatibilitySearchResponse>("/api/v1/compatibility/games?q=GTA%20V"))!;
        Check(gta.Items.Length == 2 && gta.Items.Select(x => x.Game.SteamAppId).Distinct().Count() == 2, "GTA editions distinct over wire");
        var response = await client.PostAsJsonAsync("/api/v1/compatibility/tests", input);
        Check(response.IsSuccessStatusCode, "real submission accepted");
        var first = (await response.Content.ReadFromJsonAsync<CompatibilityTest>())!;
        Check(first.Environment == environment && first.Result == "success" && first.GameId == input.GameId, "environment survives JSON round trip");
        Check(!(await response.Content.ReadAsStringAsync()).Contains("http-test@example.test") && first.Tester.StartsWith("tester-"), "public test never returns account email");
        var replay = await client.PostAsJsonAsync("/api/v1/compatibility/tests", input);
        Check(replay.IsSuccessStatusCode && (await replay.Content.ReadFromJsonAsync<CompatibilityTest>())!.Id == first.Id, "HTTP retry idempotent");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { Result = "failure" }), HttpStatusCode.Conflict, "submission_conflict");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { SubmissionId = Guid.NewGuid().ToString("N") }), HttpStatusCode.Conflict, "environment_already_reported");
        var detail = (await client.GetFromJsonAsync<CompatibilityDetail>("/api/v1/compatibility/games/cyberpunk-2077?gpu=RX%207900%20XT"))!;
        Check(detail.Counts.Total == 1 && detail.Status == "success" && detail.Tests.Single().Id == first.Id, "aggregate reflects one real submission");
        var otherGpu = (await client.GetFromJsonAsync<CompatibilityDetail>("/api/v1/compatibility/games/cyberpunk-2077?gpu=RX%207800%20XT"))!;
        Check(otherGpu.Counts.Total == 0 && otherGpu.Status == "untested" && otherGpu.Tests.Length == 0 && otherGpu.Gpus.Length == 1, "GPU filter cannot borrow other hardware results");
        await Error(await client.GetAsync("/api/v1/compatibility/games/not-found"), HttpStatusCode.NotFound, "game_not_found");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { SubmissionId = Guid.NewGuid().ToString("N"), GameId = "gta-v-enhanced" }), HttpStatusCode.Conflict, "game_identity_mismatch");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { Environment = null! }), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { Environment = environment with { Gpu = null! } }), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with { Notes = new string('x', 2001) }), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        Check((await client.PostAsync("/api/v1/compatibility/tests", new StringContent("{broken", Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "malformed JSON rejected");
        async Task Policy(string key, bool enabled, string tier)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AccountManagementService>().SetFeatureAsync(key, enabled, tier, "HTTP compatibility policy", "test-admin");
        }
        await Policy("compatibility.read", false, "standard");
        await Error(await client.GetAsync("/api/v1/compatibility/games"), HttpStatusCode.Forbidden, "feature_disabled");
        await Error(await client.GetAsync("/api/v1/compatibility/games/cyberpunk-2077"), HttpStatusCode.Forbidden, "feature_disabled");
        await Policy("compatibility.read", true, "pro");
        await Error(await client.GetAsync("/api/v1/compatibility/games"), HttpStatusCode.Forbidden, "pro_required");
        await Policy("compatibility.read", true, "standard");
        await Policy("compatibility.submit", false, "standard");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input), HttpStatusCode.Forbidden, "feature_disabled");
        await Policy("compatibility.submit", true, "pro");
        await Error(await client.PostAsJsonAsync("/api/v1/compatibility/tests", input), HttpStatusCode.Forbidden, "pro_required");
        await Policy("compatibility.submit", true, "standard");
        var unknown = await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with
        {
            SubmissionId = Guid.NewGuid().ToString("N"), Environment = environment with { GameVersion = "unknown", RenderApi = "unknown" },
            Result = "partial", Notes = null
        });
        Check(unknown.IsSuccessStatusCode && (await unknown.Content.ReadFromJsonAsync<CompatibilityTest>())!.FailureReason == "unknown", "unknown environment and problem reason stay explicit");
        Console.WriteLine($"Compatibility HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
