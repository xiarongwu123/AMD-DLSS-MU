using System.Text.Json;
using System.Text.Json.Nodes;
using Mu.Compatibility;
using Mu.Server.Data;

internal static class CompatibilityMetadataTests
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Compatibility metadata: " + message);
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { Check(true, message); return; }
            throw new InvalidOperationException("Compatibility metadata accepted " + message);
        }
        var sha = new string('a', 64);
        var commit = new string('b', 40);
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        var fixture = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            games = new[] { new
            {
                steamAppId = "1091500", name = "Cyberpunk 2077", status = "available", reason = (string?)null,
                sourceUrl = "https://store.steampowered.com/api/appdetails?appids=1091500&l=english&cc=us",
                sourceRetrievedAt = at, sourceSha256 = sha,
                minimum = new CompatibilityRequirementsTier("Minimum:\nMemory: 12 GB RAM", "Windows 10 64-bit", null,
                    "12 GB RAM", null, "12", "70 GB available space", null, 12288, 71680),
                recommended = (CompatibilityRequirementsTier?)null
            } }
        });
        var parsed = CompatibilityMetadata.ParseRequirements(fixture)["1091500"];
        Check(parsed.Status == "available" && parsed.Minimum?.MemoryMb == 12288 && parsed.Recommended == null,
            "source quantities retained while unspecified recommendation stays null");
        Check(parsed.Minimum?.Text?.Contains('\n') == true && parsed.SourceSha256 == sha, "plaintext and audit digest retained");
        void BadRequirements(Action<JsonObject> mutate, string description)
        {
            var root = JsonNode.Parse(fixture)!.AsObject();
            mutate(root);
            Reject(() => CompatibilityMetadata.ParseRequirements(root.ToJsonString()), description);
        }
        BadRequirements(root => root["games"]![0]!["sourceUrl"] = "https://store.steampowered.com.attacker.test/app/1091500/", "lookalike source host");
        BadRequirements(root => root["games"]![0]!["sourceUrl"] = "https://store.steampowered.com/api/appdetails?appids=271590", "source/game mismatch");
        BadRequirements(root => root["games"]![0]!["sourceUrl"] = "https://store.steampowered.com/api/appdetails?appids=1091500&appids=271590", "duplicate source identity parameter");
        BadRequirements(root => root["games"]![0]!["sourceSha256"] = "invalid", "missing provenance hash");
        BadRequirements(root => root["games"]![0]!["minimum"]!["MemoryMb"] = -1, "negative numeric memory");
        BadRequirements(root => root["games"]![0]!["minimum"]!["Memory"] = null, "numeric value without source field");
        BadRequirements(root => root["games"]![0]!["status"] = "not_provided", "absent status with present requirements");
        BadRequirements(root => root["games"]![0]!["minimum"]!["Text"] = "bad\0text", "control characters");
        BadRequirements(root => root["games"]!.AsArray().Add(root["games"]![0]!.DeepClone()), "duplicate game identity");
        BadRequirements(root => root["games"] = null, "null games array");
        BadRequirements(root => root["games"]![0] = null, "null game entry");
        var missing = JsonNode.Parse(fixture)!;
        missing["games"]![0]!["status"] = "unavailable";
        missing["games"]![0]!["reason"] = "source_request_failed";
        missing["games"]![0]!["sourceSha256"] = null;
        missing["games"]![0]!["minimum"] = null;
        Check(CompatibilityMetadata.ParseRequirements(missing.ToJsonString())["1091500"] is { Status: "unavailable", Minimum: null },
            "source failure remains explicit unknown, never fabricated requirements");

        var adaptations = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = new { provider = "OptiScaler Wiki", url = $"https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List/{commit}", commit, retrievedAt = at, sha256 = sha },
            entries = new[] { new
            {
                id = "optiscaler-fixture", steamAppId = "1091500", name = "Cyberpunk 2077", sourceProvider = "OptiScaler Wiki",
                sourceUrl = $"https://github.com/optiscaler/OptiScaler/wiki/Cyberpunk-2077/{commit}",
                sourceRetrievedAt = at, sourceCommit = commit, status = "working", upscalerInputs = new[] { "FSR 2+" },
                requiredMod = "none", notes = new[] { "Upstream observation only." },
                testEnvironment = new CompatibilityModEnvironment(null, null, null), muVerified = false
            } }
        });
        var mod = CompatibilityMetadata.ParseAdaptations(adaptations)["1091500"].Single();
        Check(mod.Status == "working" && !mod.MuVerified && mod.TestEnvironment?.Gpu == null,
            "upstream success retains MU verification and hardware unknowns");
        void BadAdaptation(Action<JsonObject> mutate, string description)
        {
            var root = JsonNode.Parse(adaptations)!.AsObject();
            mutate(root);
            Reject(() => CompatibilityMetadata.ParseAdaptations(root.ToJsonString()), description);
        }
        BadAdaptation(root => root["entries"]![0]!["muVerified"] = true, "invented MU verification");
        BadAdaptation(root => root["entries"]![0]!["sourceUrl"] = "https://github.com/other/OptiScaler/wiki/Compatibility-List/" + commit, "untrusted wiki owner");
        BadAdaptation(root => root["entries"]![0]!["sourceUrl"] = "https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List", "mutable wiki source");
        BadAdaptation(root => root["entries"]![0]!["sourceCommit"] = new string('c', 40), "mismatched source revision");
        BadAdaptation(root => root["entries"]![0]!["status"] = "mu_verified", "invented compatibility state");
        BadAdaptation(root => root["entries"]![0]!["steamAppId"] = "999999999", "unreviewed Steam mapping");
        BadAdaptation(root => root["entries"]![0]!["notes"] = null, "null notes");
        BadAdaptation(root => root["entries"]!.AsArray().Add(root["entries"]![0]!.DeepClone()), "duplicate source entry");
        BadAdaptation(root => root["source"] = null, "null source audit");
        var unmatched = JsonNode.Parse(adaptations)!;
        unmatched["entries"]![0]!["steamAppId"] = null;
        Check(CompatibilityMetadata.ParseAdaptations(unmatched.ToJsonString()).Count == 0, "unmatched title never acquires a guessed Steam identity");
        Check(CompatibilityMetadata.Requirements(null) == null && CompatibilityMetadata.Adaptations(null).Length == 0
            && CompatibilityMetadata.Evidence(null) == new CompatibilityEvidence(false, null), "custom local games have no invented source metadata");
        var oldDetail = JsonSerializer.Deserialize<CompatibilityDetail>("""
            {"Game":{"Id":"local","Name":"Local","SteamAppId":null},"Counts":{"Success":0,"Partial":0,"Failure":0},"Status":"untested","Gpus":[],"Tests":[]}
            """);
        Check(oldDetail?.Requirements == null && oldDetail?.ModCompatibility == null, "new DTO still reads the old detail response");
        Check(JsonSerializer.Deserialize<CompatibilitySearchResponse>("{\"Items\":[]}")?.Coverage == null,
            "old search payload omits coverage without implying zero evidence");
        Check(CompatibilityMetadata.Coverage([null, "unknown", "1091500", "1091500"])
            == new CompatibilityCoverage(1, 1), "coverage counts distinct matched catalog games, not source entries or unknown identities");
        Console.WriteLine($"Compatibility metadata passed: {assertions} assertions.");
        return assertions;
    }
}
