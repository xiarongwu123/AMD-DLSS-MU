using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Mu.Compatibility;
using Mu.Server;
using Mu.Server.Services;

internal static class PublicCompatibilityHttpTests
{
    private sealed record GpuResponse(string[] Items);

    public static async Task<int> RunAsync()
    {
        using var factory = new AccountFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        const string root = "/api/v1/compatibility/public";
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Public compatibility HTTP: " + message);
        }
        async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Check(response.StatusCode == status, $"expected {status}, got {response.StatusCode}");
            Check((await response.Content.ReadFromJsonAsync<ApiError>())?.Code == code, "stable error code " + code);
        }
        async Task Policy(string key, bool enabled, string tier = "standard")
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AccountManagementService>().SetFeatureAsync(key, enabled, tier, "Public compatibility HTTP test", "test-admin");
        }
        var initial = (await anonymous.GetFromJsonAsync<CompatibilitySearchResponse>(root + "/games"))!;
        Check(initial.CatalogTotal >= 4 && initial.Total == initial.CatalogTotal && initial.Items.Length == Math.Min(50, initial.CatalogTotal)
            && initial.Items.All(x => x.Counts.Total == 0 && x.Status == "untested" && x.LastTestedAt == null), "anonymous catalog has zero fabricated observations");
        Check((await anonymous.GetFromJsonAsync<GpuResponse>(root + "/gpus"))!.Items.Length == 0, "empty catalog has no synthetic GPU suggestions");
        var noTests = (await anonymous.GetFromJsonAsync<CompatibilityDetail>(root + "/games/cyberpunk-2077"))!;
        Check(noTests.Counts.Total == 0 && noTests.Status == "untested" && noTests.Tests.Length == 0 && noTests.Gpus.Length == 0, "anonymous detail shows untested state");
        Check(noTests.Requirements is { Provider: "Steam Store", Status: "available", Minimum.Graphics: not null, Recommended.Graphics: not null }
            && noTests.Requirements.SourceUrl == "https://store.steampowered.com/app/1091500/", "anonymous detail exposes exact-game published requirements");
        Check(noTests.ModCompatibility is { Length: > 0 } && noTests.ModCompatibility.All(x => !x.MuVerified
            && x.SourceUrl.StartsWith("https://github.com/optiscaler/OptiScaler/wiki/", StringComparison.Ordinal)),
            "public adaptation facts retain upstream provenance and are never labeled MU-verified");
        var sourcedResponse = await anonymous.GetAsync(root + "/games?q=1091500&gpu=unknown-hardware");
        var sourcedSearch = (await sourcedResponse.Content.ReadFromJsonAsync<CompatibilitySearchResponse>())!;
        var sourced = sourcedSearch.Items.Single();
        Check(initial.Coverage is { Requirements: >= 4, ModCompatibility: >= 200 } && sourcedSearch.Coverage == initial.Coverage,
            "global public evidence coverage is unaffected by text or GPU filters");
        Check(sourced.Evidence is { HasRequirements: true, ModStatus: "working" } && sourced.Status == "untested" && sourced.Counts.Total == 0,
            "list has source coverage without claiming a test or local hardware success");
        var sourcedJson = await sourcedResponse.Content.ReadAsStringAsync();
        using var sourcedDocument = JsonDocument.Parse(sourcedJson);
        var listedItem = sourcedDocument.RootElement.GetProperty("items")[0];
        Check(!listedItem.TryGetProperty("requirements", out _) && !listedItem.TryGetProperty("modCompatibility", out _),
            "list payload excludes full detail evidence");
        Check((await anonymous.GetAsync("/api/v1/compatibility/games")).StatusCode == HttpStatusCode.Unauthorized, "private search remains authenticated");
        Check((await anonymous.GetAsync("/api/v1/compatibility/games/cyberpunk-2077")).StatusCode == HttpStatusCode.Unauthorized, "private detail remains authenticated");
        var env = new CompatibilityEnvironment(new("RX 7900 XT", "AMD", 20480, "RDNA 3", "26.8.1"),
            "Windows 11", "12", "2.3", "DX12", "unknown", "unknown", "2.0.0", "1440p", "unknown");
        var input = new CompatibilitySubmission(Guid.NewGuid().ToString("N"), "cyberpunk-2077", "Cyberpunk 2077", "1091500", env, "success", null, null);
        Check((await anonymous.PostAsJsonAsync("/api/v1/compatibility/tests", input)).StatusCode == HttpStatusCode.Unauthorized, "anonymous reporting remains forbidden");
        Check((await anonymous.PostAsJsonAsync(root + "/tests", input)).StatusCode == HttpStatusCode.NotFound, "no public report submission route exists");
        Check((await anonymous.PostAsJsonAsync(root + "/games", input)).StatusCode == HttpStatusCode.MethodNotAllowed, "public catalog is read-only");

        const string email = "public-read-test@example.test";
        Check((await client.PostAsJsonAsync("/api/v1/auth/code", new { email, purpose = "register" })).IsSuccessStatusCode, "test account challenge accepted");
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, code = factory.Mail.Codes[email + ":register"], password = "public-read-test-password",
            termsVersion = AccountService.TermsVersion, deviceName = "HTTP public compatibility test"
        });
        Check(registration.IsSuccessStatusCode, "test account registered");
        var session = (await registration.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var privateSearch = (await client.GetFromJsonAsync<CompatibilitySearchResponse>("/api/v1/compatibility/games?q=missing-coverage-game&page=2&pageSize=1"))!;
        Check(privateSearch.Total == 0 && privateSearch.Items.Length == 0 && privateSearch.Coverage == initial.Coverage,
            "authenticated empty search has the same global source coverage");
        async Task<CompatibilityTest> Submit(CompatibilityEnvironment environment, string result)
        {
            var response = await client.PostAsJsonAsync("/api/v1/compatibility/tests", input with
            { SubmissionId = Guid.NewGuid().ToString("N"), Environment = environment, Result = result });
            Check(response.IsSuccessStatusCode, "authenticated report accepted");
            return (await response.Content.ReadFromJsonAsync<CompatibilityTest>())!;
        }
        var first = await Submit(env, "success");
        await Submit(env with { Gpu = env.Gpu with { Name = "rx 7900 xt", DriverVersion = "26.8.2" } }, "partial");
        await Submit(env with { Gpu = env.Gpu with { Name = "RX 7800 XT" } }, "failure");
        await Submit(env with { Gpu = env.Gpu with { Name = "unknown" } }, "failure");
        var gpus = (await anonymous.GetFromJsonAsync<GpuResponse>(root + "/gpus"))!.Items;
        Check(gpus.SequenceEqual(new[] { "RX 7800 XT", "RX 7900 XT" }), "GPU suggestions use distinct real models, sorted, excluding unknown");
        var detailResponse = await anonymous.GetAsync(root + "/games/cyberpunk-2077?gpu=rx%207900%20xt");
        Check(detailResponse.IsSuccessStatusCode, "anonymous detail returns 200");
        var detail = (await detailResponse.Content.ReadFromJsonAsync<CompatibilityDetail>())!;
        Check(detail.Counts == new CompatibilityCounts(1, 1, 0) && detail.Status == "mixed" && detail.Tests.Length == 2 && detail.Gpus.Length == 3, "public GPU filtering preserves complete GPU overview");
        var privateDetail = (await client.GetFromJsonAsync<CompatibilityDetail>("/api/v1/compatibility/games/cyberpunk-2077?gpu=RX%207900%20XT"))!;
        Check(JsonSerializer.Serialize(detail) == JsonSerializer.Serialize(privateDetail), "public and client queries share the same aggregation contract");
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        Check(!detailJson.Contains(email, StringComparison.OrdinalIgnoreCase) && !detailJson.Contains(session.Account.Id, StringComparison.Ordinal)
            && detail.Tests.Any(x => x.Id == first.Id && x.Tester == first.Tester), "public reports expose pseudonym without email or account ID");
        var search = (await anonymous.GetFromJsonAsync<CompatibilitySearchResponse>(root + "/games?q=1091500&gpu=RX%207800%20XT"))!;
        Check(search.Items.Single().Counts == new CompatibilityCounts(0, 0, 1) && search.Items.Single().Status == "failure", "public search applies exact GPU filter");
        var unmatched = (await anonymous.GetFromJsonAsync<CompatibilityDetail>(root + "/games/cyberpunk-2077?gpu=RX%209070%20XT"))!;
        Check(unmatched.Status == "untested" && unmatched.Counts.Total == 0 && unmatched.Tests.Length == 0 && unmatched.LastTestedAt == null, "untested hardware does not inherit other GPUs' outcomes");
        foreach (var value in new[] { "' OR 1=1; DROP TABLE CompatibilityTests; --", "%", "<script>alert(1)</script>" })
        {
            var result = (await anonymous.GetFromJsonAsync<CompatibilitySearchResponse>(root + "/games?q=" + Uri.EscapeDataString(value)))!;
            Check(value == "%" ? result.Total < initial.CatalogTotal : result.Items.Length == 0,
                "query is a bound literal, not SQL, wildcard or markup: " + value);
        }
        Check((await anonymous.GetFromJsonAsync<CompatibilityDetail>(root + "/games/cyberpunk-2077"))!.Counts.Total == 4, "hostile queries cannot mutate reports");
        await Error(await anonymous.GetAsync(root + "/games?q=" + new string('q', 201)), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        await Error(await anonymous.GetAsync(root + "/games?gpu=" + new string('g', 161)), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        await Error(await anonymous.GetAsync(root + "/games/" + new string('i', 65)), HttpStatusCode.BadRequest, "invalid_compatibility_submission");
        await Error(await anonymous.GetAsync(root + "/games/missing"), HttpStatusCode.NotFound, "game_not_found");

        await Policy("compatibility.public.read", false);
        foreach (var route in new[] { "/games", "/games/cyberpunk-2077", "/gpus" })
            await Error(await anonymous.GetAsync(root + route), HttpStatusCode.Forbidden, "feature_disabled");
        Check((await client.GetAsync("/api/v1/compatibility/games")).IsSuccessStatusCode, "public switch does not disable authenticated client query");
        Check((await anonymous.GetAsync("/api/health")).IsSuccessStatusCode, "public switch does not affect health");
        await Policy("compatibility.public.read", true, "pro");
        Check((await anonymous.GetAsync(root + "/games")).IsSuccessStatusCode, "public switch has no membership interpretation even if a stored tier is Pro");
        await Policy("compatibility.public.read", true);
        await Policy("compatibility.read", false);
        await Error(await client.GetAsync("/api/v1/compatibility/games"), HttpStatusCode.Forbidden, "feature_disabled");
        Check((await anonymous.GetAsync(root + "/games")).IsSuccessStatusCode, "private read policy does not accidentally control public visibility");
        await Policy("compatibility.read", true);

        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 61; attempt++)
        {
            var response = await anonymous.GetAsync(root + "/gpus");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = response; break; }
            Check(response.IsSuccessStatusCode, "public request succeeds within dedicated budget");
        }
        Check(limited != null, "public limiter rejects repeated reads before global budget is exhausted");
        await Error(limited!, HttpStatusCode.TooManyRequests, "rate_limited");
        Check(limited!.Headers.RetryAfter != null, "anonymous limiter provides Retry-After");
        Check((await anonymous.GetAsync("/api/health")).IsSuccessStatusCode, "public limiter does not affect readiness endpoint");
        Check((await client.GetAsync("/api/v1/compatibility/games")).IsSuccessStatusCode, "public limiter does not consume authenticated route budget");
        Check((await anonymous.PostAsJsonAsync("/api/v1/compatibility/tests", input)).StatusCode == HttpStatusCode.Unauthorized, "public rate saturation never opens anonymous writes");
        Console.WriteLine($"Public compatibility HTTP passed: {assertions} assertions.");
        return assertions;
    }
}
