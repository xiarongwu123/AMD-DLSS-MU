using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmdNrAssistant;
using Mu.Neural;
using Mu.Recommendation;

static class AmdNrRdna3Tests
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception("AMDNR RDNA3: " + message); count++; }
    static void Reject(Action action, string message)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException) { count++; return; } throw new Exception(message); }
    sealed class Progress(Action<string> action) : IProgress<string> { public void Report(string message) => action(message); }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Pe(bool dll)
    {
        var b = new byte[512]; b[0] = 0x4d; b[1] = 0x5a; BitConverter.GetBytes(0x80).CopyTo(b, 0x3c);
        b[0x80] = 0x50; b[0x81] = 0x45; BitConverter.GetBytes((ushort)0x8664).CopyTo(b, 0x84);
        BitConverter.GetBytes((ushort)(dll ? 0x2000 : 0)).CopyTo(b, 0x96); return b;
    }
    static EnvironmentSnapshot EnvironmentFor(string arch) => new() { BackupReady = true,
        Fields = new() { ["gpuVendor"] = new("AMD", "CONFIRMED", "fixture"), ["architecture"] = new(arch, "INFERRED", "fixture"),
            ["api"] = new("dx12", "CONFIRMED", "fixture"), ["bitness"] = new("64", "CONFIRMED", "fixture"),
            ["os"] = new("windows", "CONFIRMED", "fixture"), ["osBuild"] = new("22631", "CONFIRMED", "fixture"),
            ["driver"] = new("32.0.1.0", "CONFIRMED", "fixture") },
        Resources = new() { ["hip"] = true, ["amdnr-rdna3-package-preparable"] = true } };
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = NeuralRenderPlanner.Select("steam:fixture", "AMD Radeon RX 7800 XT", "dx12", null, new(), now);
        foreach (var mode in new[] { 0, 4, 5, 8 })
            Check(RenderSchemes.PreflightBackend("compat", mode, "rdna3", "auto") == "daniel", "Original mode routed to AMDNR");
        Check(RenderSchemes.PreflightBackend("compat", 6, "rdna3", "auto") == NeuralBackends.LmxxfRdna3, "Compatibility preflight chooses wrong package");
        Check(RenderSchemes.PreflightBackend("compat", 6, "rdna3", "daniel") == "daniel", "Manual Daniel selection changed");
        Check(RenderSchemes.PreflightBackend("online", 6, "rdna3", "auto") == "daniel", "Online default changed");
        Check(plan.Backend == NeuralBackends.LmxxfRdna3 && plan.PackageVersion == "0.3.5.8" && plan.Status == "experimental", "Wrong automatic RX7000 route or evidence");
        Check(NeuralRenderPlanner.Select("steam:fixture", "RX 6750 XT", "dx12", null, new(), now).Backend == "daniel", "RX6000 route changed");
        Check(NeuralRenderPlanner.Select("steam:fixture", "RX 9070 XT", "dx12", null, new(), now).Backend == "daniel", "Original RX9000 default changed");
        foreach (var gpu in new[] { "RX 6750 XT", "RX 9070 XT" })
            Reject(() => NeuralRenderPlanner.Select("steam:fixture", gpu, "dx12", null, new(NeuralBackends.LmxxfRdna3), now), "RDNA3 package accepted wrong GPU");
        Reject(() => NeuralRenderPlanner.Select("steam:fixture", "RX 7900 XT", "dx12", null, new(NeuralBackends.LmxxfRdna3, Passes: 4), now), "RDNA3 accepted 4 passes");
        Reject(() => NeuralRenderPlanner.Select("steam:fixture", "RX 7900 XT", "dx12", null, new(NeuralBackends.LmxxfRdna3, NrInline: true), now), "Foreign ABI options accepted");
        Check(NeuralRenderInstaller.MissingResources("unused.exe", plan).Length == 0, "RDNA3 requests RDNA4 loose model files");
        Check(NeuralRenderInstaller.ManagedPath("LmxxfNrRuntime.pak") && !NeuralRenderInstaller.ManagedPath("../LmxxfNrRuntime.pak"), "Pak ownership paths incorrect");
        var settings = NeuralRenderPlanner.Settings(plan);
        Check(settings["NrBackend"] == "lmxxf" && settings["AmdLmxxfFastMode"] == "true" && !settings.ContainsKey("NrInline"), "INI contract mixes hosts");
        var cap = CapabilityCatalog.For("compat", 64, NeuralBackends.LmxxfRdna3);
        var env = EnvironmentFor("rdna3");
        Check(HardConstraintEvaluator.Evaluate(env, cap, plan.Backend, null, [], now).Length == 0, "RX7000 preflight blocked a preparable package");
        var win10 = env with { Fields = new(env.Fields) { ["osBuild"] = new("19045", "CONFIRMED", "fixture") } };
        Check(!HardConstraintEvaluator.Evaluate(win10, cap, plan.Backend, null, [], now).Any(i => i.Code == "OS_VERSION"), "AMDNR Windows 10 support blocked");
        Check(HardConstraintEvaluator.Evaluate(env, CapabilityCatalog.For("online"), plan.Backend, null, [], now).Any(i => i.Code == "SCHEME_INCOMPATIBLE"), "RDNA3 host used by a fixed route");
        foreach (var arch in new[] { "rdna2", "rdna4" })
            Check(HardConstraintEvaluator.Evaluate(EnvironmentFor(arch), cap, plan.Backend, null, [], now).Any(i => i.Code == "ARCH_INCOMPATIBLE"), "Wrong architecture permitted");
        Check(HardConstraintEvaluator.Evaluate(env, CapabilityCatalog.For("compat"), "lmxxf", null, [], now).Any(i => i.Code == "ARCH_INCOMPATIBLE"), "Original RDNA4 guard removed");
        var config = ParameterResolver.Resolve(cap, env, null, plan.Backend);
        Check(config.ComponentVersion == plan.PackageVersion && config.Ini["DlssNr"].OrderBy(p => p.Key).SequenceEqual(settings.OrderBy(p => p.Key)), "Recommendation and installer disagree");
        var rule = new SmartCompatibilityRule { Id = "fixture-rdna3", Version = "v1", GameId = "steam:fixture", Scheme = "compat",
            Backend = plan.Backend, Architectures = ["rdna3"], ComponentVersion = plan.PackageVersion, Source = "https://example.test/fixture",
            VerifiedAt = now.AddDays(-1), ExpiresUtc = now.AddDays(1), Dependencies = ["hip", "amdnr-rdna3-package"] };
        RecommendationRuleSchema.Validate(rule); count++;
        Reject(() => RecommendationRuleSchema.Validate(rule with { ComponentVersion = "1.12.1" }), "Host version mismatch permitted");
        Reject(() => RecommendationRuleSchema.Validate(rule with { Architectures = ["rdna2"] }), "Remote rule expands RX6000 support");
        var candidates = CandidateGenerator.Generate(env, [], new(), "stability", now, "compat");
        Check(candidates.Single().Configuration.Backend == plan.Backend && !candidates.Single().Automatic, "Unverified generic route automatically endorsed");
        var pin = AmdNrRdna3Package.Pin();
        Check(pin.Version == plan.PackageVersion && pin.Size == 1008053921 && pin.Files.ContainsKey("LmxxfNrRuntime.pak"), "Real resource pin missing");
        Check(!pin.Files.Keys.Any(n => n.Contains("Launcher", StringComparison.OrdinalIgnoreCase) || n.StartsWith("FSR4Fast/")), "Optional programs included");
        Transactions();
        Console.WriteLine($"AMDNR RDNA3 policy/package/transactions: {count} assertions passed; no GPU execution.");
    }
    static void Transactions()
    {
        var oldStore = GameManagement.StorageOverride; var oldResources = NeuralRenderInstaller.ResourcesOverride;
        var root = Path.Combine(Path.GetTempPath(), "mu-rdna3-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            GameManagement.StorageOverride = Path.Combine(root, "records"); NeuralRenderInstaller.ResourcesOverride = Path.Combine(root, "no-user-resources");
            var files = new Dictionary<string, byte[]> { ["OptiScaler.dll"] = Pe(true), ["LmxxfNrRuntime.dll"] = Pe(true),
                ["LmxxfNrRuntime.pak"] = Encoding.UTF8.GetBytes("synthetic transaction fixture, not a working GPU model"),
                ["OptiScaler.ini"] = Encoding.UTF8.GetBytes("[DlssNr]\nEnabled=auto\n"), ["OptiScaler/libxess.dll"] = Pe(true),
                ["Licenses/AMDNR_NOTICE.txt"] = Encoding.UTF8.GetBytes("test attribution"), ["LICENSE"] = Encoding.UTF8.GetBytes("test licence") };
            var zip = Path.Combine(root, "fixture.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var pair in files) { using var output = archive.CreateEntry(pair.Key).Open(); output.Write(pair.Value); }
                using var script = new StreamWriter(archive.CreateEntry("Setup.bat").Open()); script.Write("must not be deployed");
            }
            AmdNrRdna3Package.PinOverride = new("0.3.5.8", "https://example.test/fixture.zip", new FileInfo(zip).Length, Core.Hash(zip),
                files.ToDictionary(p => p.Key, p => new NeuralRenderInstaller.FilePin(p.Value.Length, Hash(p.Value))));
            var package = Path.Combine(root, "package"); NeuralRenderInstaller.ExtractVerified(zip, package, NeuralBackends.LmxxfRdna3);
            Check(!File.Exists(Path.Combine(package, "Setup.bat")), "Setup script extracted");
            var game = Path.Combine(root, "game"); Directory.CreateDirectory(game); var exe = Path.Combine(game, "fixture.exe"); File.WriteAllBytes(exe, Pe(false));
            var ini = Path.Combine(game, "OptiScaler.ini");
            const string original = "[DlssNr]\nEnabled=false\nNrBackend=daniel\n[XeFG]\nInterpolationCount=4\n[FrameGen]\nEnabled=true\n[User]\nKeep=hello\n";
            File.WriteAllText(ini, original);
            var plan = NeuralRenderPlanner.Select("steam:fixture", "RX 7800 XT", "dx12", null, new(NeuralBackends.LmxxfRdna3), DateTimeOffset.UtcNow);
            var proxy = Path.Combine(game, "dxgi.dll"); File.WriteAllText(proxy, "foreign");
            Reject(() => NeuralRenderInstaller.Install(exe, package, plan), "Foreign proxy overwritten"); File.Delete(proxy);
            File.AppendAllText(Path.Combine(package, "LmxxfNrRuntime.dll"), "wrong version");
            Reject(() => NeuralRenderInstaller.Install(exe, package, plan), "Mismatched runtime deployed"); File.WriteAllBytes(Path.Combine(package, "LmxxfNrRuntime.dll"), files["LmxxfNrRuntime.dll"]);
            Check(!File.Exists(proxy) && File.ReadAllText(ini) == original, "Failed validation changed game");
            NeuralRenderInstaller.Install(exe, package, plan);
            var record = GameManagement.Read(exe)!;
            Check(record.Version == "AMDNR 0.3.5.8" && record.Neural?.Backend == plan.Backend && record.Neural.PackageVersion == "0.3.5.8", "Manifest identifies wrong host");
            Check(File.Exists(Path.Combine(game, "LmxxfNrRuntime.pak")) && record.Files.All(f => f.After == Core.Hash(Path.Combine(game, f.Name))), "Installed files differ from manifest");
            Check(File.ReadAllText(ini).Contains("InterpolationCount=4") && File.ReadAllText(ini).Contains("Keep=hello"), "Existing FG/custom values lost");
            var edited = plan with { Passes = 3, Options = plan.Options with { Passes = 3, LmxxfFastMode = false } };
            NeuralRenderInstaller.UpdateSettings(exe, edited);
            var snapshot = CommunityPresetConfig.Capture(exe);
            var roundtrip = JsonSerializer.Deserialize<Mu.Presets.PresetSetting[]>(JsonSerializer.Serialize(snapshot.Settings))!;
            var options = CommunityPresetConfig.NeuralOptionsFromPreset(roundtrip, snapshot.Record.Version);
            Check(options.Backend == plan.Backend && options.Passes == 3 && options.LmxxfFastMode == false, "Sharing loses runtime family or fast mode");
            Check(ManagedIni.Get(File.ReadAllText(ini), "DlssNr", "Passes") == "3" && ManagedIni.Get(File.ReadAllText(ini), "DlssNr", "AmdLmxxfFastMode") == "false", "Advanced settings not saved");
            GameManagement.Restore(exe);
            Check(File.ReadAllText(ini) == original && !File.Exists(proxy) && !File.Exists(Path.Combine(game, "LmxxfNrRuntime.pak")) && !Directory.Exists(Path.Combine(game, "OptiScaler")), "Restore leaves components or destroys INI");
            using (var cancel = new CancellationTokenSource())
                Reject(() => NeuralRenderInstaller.Install(exe, package, plan, cancel.Token, new Progress(_ => cancel.Cancel())), "Cancellation ignored");
            Check(GameManagement.Read(exe)!.Phase == "restored" && !File.Exists(proxy) && File.ReadAllText(ini) == original, "Cancellation did not roll back");
            NeuralRenderInstaller.Install(exe, package, plan);
            var pak = Path.Combine(game, "LmxxfNrRuntime.pak"); File.AppendAllText(pak, "another tool modified this");
            Reject(() => GameManagement.Restore(exe), "Foreign changed pak removed");
            Check(File.Exists(proxy), "Restore mutated before detecting foreign pak"); File.WriteAllBytes(pak, files["LmxxfNrRuntime.pak"]); GameManagement.Restore(exe);
        }
        finally { AmdNrRdna3Package.PinOverride = null; GameManagement.StorageOverride = oldStore; NeuralRenderInstaller.ResourcesOverride = oldResources; Directory.Delete(root, true); }
    }
    public static void RealPackage(string package, string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); GameManagement.StorageOverride = Path.Combine(root, "records");
        NeuralRenderInstaller.ValidatePackage(package, NeuralBackends.LmxxfRdna3);
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game); var exe = Path.Combine(game, "fixture.exe"); File.WriteAllBytes(exe, Pe(false));
        var ini = Path.Combine(game, "OptiScaler.ini"); const string original = "[XeFG]\nInterpolationCount=4\n[FrameGen]\nEnabled=true\n[User]\nKeep=original\n"; File.WriteAllText(ini, original);
        var plan = NeuralRenderPlanner.Select("steam:fixture", "RX 7800 XT", "dx12", null, new(NeuralBackends.LmxxfRdna3), DateTimeOffset.UtcNow);
        NeuralRenderInstaller.Install(exe, package, plan);
        var record = GameManagement.Read(exe)!;
        Check(record.Neural?.Backend == NeuralBackends.LmxxfRdna3 && record.Files.All(f => Core.Hash(Path.Combine(game, f.Name)) == f.After), "Real host/runtime/pak manifest mismatch");
        Check(File.Exists(Path.Combine(game, "Licenses/AMDNR_RDNA3_BACKEND.txt")) && File.Exists(Path.Combine(game, "Licenses/AMDNR-README.md")), "Original notices not installed");
        Check(!File.Exists(Path.Combine(game, "Setup.bat")) && !File.Exists(Path.Combine(game, "AMDNR-Launcher.exe")) && !Directory.Exists(Path.Combine(game, "FSR4Fast")), "Unselected tools installed");
        NeuralRenderInstaller.UpdateSettings(exe, plan with { Passes = 2, Options = plan.Options with { Passes = 2 } });
        var capture = CommunityPresetConfig.Capture(exe);
        Check(CommunityPresetConfig.NeuralOptionsFromPreset(capture.Settings, capture.Record.Version).Backend == plan.Backend, "Real package share routes to wrong host");
        GameManagement.Restore(exe);
        Check(File.ReadAllText(ini) == original && !File.Exists(Path.Combine(game, "LmxxfNrRuntime.pak")) && !Directory.Exists(Path.Combine(game, "OptiScaler")), "Real package restoration incomplete");
        Console.WriteLine($"AMDNR actual 0.3.5.8 package transactions: {count} assertions passed; file operations only, no GPU/game execution.");
    }
}
