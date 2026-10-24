using System.Text.Json;
using AmdNrAssistant;
using Mu.Recommendation;

static class BugRegressionTests
{
    static int count;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Bug regression: " + message);
        count++;
    }
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (IOException) { count++; return; }
        throw new Exception("Bug regression: " + message);
    }

    public static void Run()
    {
        ContextAndBridge();
        PreparedResources();
        RestoreOwnership();
        SharedComments();
        Console.WriteLine($"Configuration bug regressions passed: {count} assertions.");
    }

    static void ContextAndBridge()
    {
        foreach (var mode in new[] { 0, 2, 4, 6 })
            foreach (var api in new[] { "dx12", "vulkan" })
                Check(InstallationContext.BridgeApi(mode, api) == "auto", "modern route incorrectly enters bridge API validation");
        foreach (var mode in new[] { 5, 8 })
        {
            Check(InstallationContext.BridgeApi(mode, "dx9") == "dx9", "legacy bridge loses its selected renderer");
            Reject(() => InstallationContext.BridgeApi(mode, "dx12"), "bridge accepted unsupported DX12 payload");
        }
        InstallationContext context;
        using (var request = JsonDocument.Parse("""{"scheme":"fps","confirmedApi":"dx12","confirmedInput":"dlss","windowed":"true"}"""))
            context = InstallationContext.Read(request.RootElement);
        // Read confirmations survive disposal of the original WebView request.
        var retry = context.Preflight(4, new(), "auto");
        Check(retry.GetProperty("scheme").GetString() == "fps", "retry changes the requested scheme");
        Check(retry.GetProperty("confirmedApi").GetString() == "dx12", "retry loses actual API confirmation");
        Check(retry.GetProperty("confirmedInput").GetString() == "dlss", "retry loses XeFG input confirmation");
        Check(retry.GetProperty("windowed").GetString() == "true", "retry loses window mode confirmation");
        var options = new NeuralOptions("mochizuki", "vulkan", Passes: 3);
        var author = new InstallationContext("author").Preflight(6, options, "auto");
        Check(author.GetProperty("scheme").GetString() == "author" && author.GetProperty("neural").GetProperty("backend").GetString() == "mochizuki",
            "retry replaces author scope or drops backend options");
        Check(author.GetProperty("confirmedApi").GetString() == "vulkan", "advanced API fallback is lost");
        var env = new EnvironmentSnapshot
        {
            BackupReady = true, Resources = new() { ["hip"] = true },
            Fields = new()
            {
                ["gpuVendor"] = new("AMD", "CONFIRMED"), ["architecture"] = new("rdna3", "INFERRED"),
                ["driver"] = new("31.0.1.2", "CONFIRMED"), ["os"] = new("windows", "CONFIRMED"),
                ["osBuild"] = new("22631", "CONFIRMED"), ["bitness"] = new("64", "CONFIRMED"),
                ["api"] = new(retry.GetProperty("confirmedApi").GetString()!, "CONFIRMED"),
                ["upscalerInput"] = new(retry.GetProperty("confirmedInput").GetString()!, "CONFIRMED")
            }
        };
        Check(!HardConstraintEvaluator.Evaluate(env, CapabilityCatalog.For("fps"), "daniel", null, [], DateTimeOffset.UtcNow)
            .Any(i => i.State is "BLOCKED" or "REQUIRE_ACTION"), "retained retry context no longer passes the real FPS preflight");
        Check(new InstallationContext().Preflight(2, new(), "auto").GetProperty("confirmedInput").GetString() == "",
            "retry invents a confirmation the user never supplied");
    }

    static void PreparedResources()
    {
        var before = new EnvironmentSnapshot
        {
            Resources = new() { ["hip"] = false, ["hip-preparable"] = true,
                ["mochizuki-model"] = false, ["mochizuki-model-preparable"] = true },
            Fields = new() { ["driver"] = new("original", "CONFIRMED") },
            Components = new() { ["dxgi.dll"] = "original-hash" }
        };
        var prepared = before with { Resources = new(before.Resources) { ["hip"] = true, ["mochizuki-model"] = true } };
        Check(EnvironmentDetector.Fingerprint(before) != EnvironmentDetector.Fingerprint(prepared), "fixture did not prepare any resources");
        Check(EnvironmentDetector.MatchesAfterPreparation(before, prepared, ["hip", "mochizuki-model"]), "verified HIP/model preparation invalidates its own plan");
        Check(!EnvironmentDetector.MatchesAfterPreparation(before, prepared, ["hip"]), "unplanned model change is silently accepted");
        Check(!EnvironmentDetector.MatchesAfterPreparation(before, prepared with { Components = new() { ["dxgi.dll"] = "changed" } }, ["hip", "mochizuki-model"]),
            "model preparation masks a changed game component");
        Check(!EnvironmentDetector.MatchesAfterPreparation(before, prepared with { Fields = new() { ["driver"] = new("changed", "CONFIRMED") } }, ["hip", "mochizuki-model"]),
            "model preparation masks a changed driver");
        Check(!EnvironmentDetector.MatchesAfterPreparation(before, prepared with { Running = true }, ["hip", "mochizuki-model"]),
            "model preparation masks game launch");
        Check(!EnvironmentDetector.MatchesAfterPreparation(before with { Resources = new(before.Resources) { ["mochizuki-model-preparable"] = false } }, prepared, ["hip", "mochizuki-model"]),
            "unavailable model preparation is accepted");
        var lost = prepared with { Resources = new(prepared.Resources) { ["hip"] = false } };
        Check(!EnvironmentDetector.MatchesAfterPreparation(prepared, lost, ["hip", "mochizuki-model"]), "disappearing dependency is ignored");
        var arbitrary = before with { Resources = new(before.Resources) { ["other"] = true, ["other-preparable"] = true } };
        Check(!EnvironmentDetector.MatchesAfterPreparation(before, arbitrary, ["other"]), "arbitrary resource name bypasses plan integrity");
    }

    static void RestoreOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "mu-bug-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = GameManagement.StorageOverride;
        GameManagement.StorageOverride = Path.Combine(root, "records");
        try
        {
            foreach (var mode in new[] { 4, 6 })
            {
                foreach (var change in new[] { "edited", "deleted", "backup-lost" })
                {
                    var exe = InstalledFixture(root, mode, change);
                    var game = Path.GetDirectoryName(exe)!;
                    var untouched = Path.Combine(game, "OptiScaler-user.ini");
                    if (change == "edited") File.WriteAllText(untouched, "user changed this file");
                    if (change == "deleted") File.Delete(untouched);
                    if (change == "backup-lost") File.Delete(Path.Combine(GameManagement.State(exe), "OptiScaler-user.ini.backup"));
                    GameManagement.Restore(exe, true);
                    Check(GameManagement.Read(exe)?.Phase == "restored" && !File.Exists(Path.Combine(game, "dxgi.dll")),
                        $"mode {mode} restore blocked by an untouched file ({change})");
                    Check(change == "deleted" ? !File.Exists(untouched) : File.ReadAllText(untouched) == (change == "edited" ? "user changed this file" : "original user data"),
                        $"mode {mode} restore rewrites someone else's data ({change})");
                }
                var protectedExe = InstalledFixture(root, mode, "changed-owned-dll");
                var dll = Path.Combine(Path.GetDirectoryName(protectedExe)!, "dxgi.dll");
                File.WriteAllText(dll, "foreign replacement");
                Reject(() => GameManagement.Restore(protectedExe, true), "restore deletes a foreign replacement DLL");
                Check(GameManagement.Read(protectedExe)?.Phase == "installed" && File.ReadAllText(dll) == "foreign replacement",
                    "failed restore mutated files or removed the recovery record");
            }
        }
        finally { GameManagement.StorageOverride = previous; Directory.Delete(root, true); }
    }

    static string InstalledFixture(string root, int mode, string name)
    {
        var game = Path.Combine(root, $"mode-{mode}-{name}"); Directory.CreateDirectory(game);
        var exe = Path.Combine(game, "fixture.exe"); File.WriteAllText(exe, "file transaction fixture; never executed");
        File.WriteAllText(Path.Combine(game, "OptiScaler-user.ini"), "original user data");
        GameManagement.Begin(exe, mode, "fixture");
        File.WriteAllText(Path.Combine(game, "dxgi.dll"), "MU installed component");
        GameManagement.Finish(exe, true);
        if (mode == 6)
        {
            var record = GameManagement.Read(exe)!;
            GameManagement.Save(record with { Neural = new("dlss5-neural-rendering", "daniel", 1, "dxgi.dll", "1.12.1", "dx12", "unverified", "fixture",
                record.Files.Select(f => new NeuralInstalledFile(f.Name, f.After, "fixture")).ToList(), [], Guid.NewGuid().ToString("N"), new("daniel")) });
        }
        return exe;
    }

    static void SharedComments()
    {
        foreach (var mode in new[] { 0, 2, 4 })
        {
            var section = mode == 0 ? "DlssNrOnAmd" : "Sharpness";
            var key = mode == 0 ? "Scale" : "Sharpness";
            var ini = $"[{section}] ; section description\r\n{key} = 0.75 ; user setting\r\nEnabled=true # another comment\r\n[Libraries]\r\nDllPath=private\\\\local.dll\r\n";
            var settings = CommunityPresetConfig.ReadFromText(mode, ini);
            Check(settings.Any(s => s.Section == section && s.Key == key && s.Value == "0.75"), $"mode {mode} sharing drops a scalar with inline comments");
            Check(settings.Any(s => s.Key == "Enabled" && s.Value == "true"), $"mode {mode} sharing drops a boolean with inline comments");
            Check(settings.All(s => s.Section != "Libraries"), "sharing leaks a private path");
            var patched = CommunityPresetConfig.Patch(mode, $"[{section}]\n{key}=0.25\nEnabled=false\n[User]\nMine=keep\n", settings);
            Check(ManagedIni.Get(patched, section, key) == "0.75" && ManagedIni.Get(patched, "User", "Mine") == "keep", "shared value was not applied or damaged unrelated settings");
        }
    }
}
