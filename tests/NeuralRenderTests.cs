using System.Text;
using AmdNrAssistant;
using Mu.Neural;
using Mu.Presets;

internal static class NeuralRenderTests
{
    sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    static int count;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
    static void Reject(Action action, string message)
    { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException) { count++; return; } throw new Exception(message); }
    public static void Run()
    {
        var original = "; keep\r\n[XeFG]\r\nInterpolationCount=4\r\n[FrameGen]\r\nEnabled=true\r\n[DlssNr]\r\n Enabled = false ; note\r\nNrBackend=daniel\r\n[User]\r\nMine = hello\r\n";
        var patched = ManagedIni.Set(original, "DlssNr", "Enabled", "true");
        Check(patched == original.Replace("Enabled = false", "Enabled = true"), "INI changed unrelated lines/comments/newlines");
        patched = ManagedIni.Set(patched, "DlssNr", "MochizukiPasses", "30");
        Check(ManagedIni.Get(patched, "DlssNr", "MochizukiPasses") == "30" && patched.Contains("[User]\r\nMine = hello"), "Missing INI key inserted into wrong section");
        Check(ManagedIni.Get(ManagedIni.Set("[Other]\na=1", "DlssNr", "Enabled", "true"), "DlssNr", "Enabled") == "true", "Missing section not appended");
        Reject(() => ManagedIni.Set("[DlssNr]\nEnabled=false\nEnabled=true", "DlssNr", "Enabled", "true"), "Duplicate INI key accepted");
        Reject(() => ManagedIni.Set("[DlssNr]\nEnabled=false\n[DlssNr]\na=1", "DlssNr", "Enabled", "true"), "Duplicate section accepted");
        Reject(() => ManagedIni.Set(original, "DlssNr", "Enabled", "true\n[evil]"), "Newline INI injection accepted");
        var unicode = ManagedIni.Encode(original, new UnicodeEncoding(false, true));
        var decoded = ManagedIni.Decode(unicode);
        Check(ManagedIni.Encode(decoded.Text, decoded.Encoding).SequenceEqual(unicode), "UTF16 BOM not preserved");
        Check(NeuralRenderPlanner.ApiFromImports(["d3d11.dll"]) == "dx11" && NeuralRenderPlanner.ApiFromImports(["d3d11.dll", "d3d12.dll"]) == "unknown", "Ambiguous graphics API guessed");
        var now = DateTimeOffset.UtcNow;
        var mochi = NeuralRenderPlanner.Select("steam:1", "AMD Radeon RX 9070 XT", "dx12", null, new(), now);
        Check(mochi.Backend == "daniel" && mochi.Passes == 1 && mochi.Status == "unverified", "GPU generation alone must not choose Mochizuki");
        var rdna3 = NeuralRenderPlanner.Select("steam:1", "AMD Radeon RX 7900 XT", "dx12", null, new(), now);
        Check(rdna3.Backend == NeuralBackends.LmxxfRdna3 && rdna3.Status == "experimental", "RX7000 auto did not choose the separate RDNA3 package");
        Reject(() => NeuralRenderPlanner.Select("steam:1", "RTX 5090", "dx12", null, new(), now), "NVIDIA accepted as AMD");
        Reject(() => NeuralRenderPlanner.Select("steam:1", "RX 9070 XT", "unknown", null, new(), now), "Unknown API guessed");
        var stress = NeuralRenderPlanner.Select("steam:1", "RX 9070 XT", "dx12", null, new("mochizuki", Passes: 30), now);
        var settings = NeuralRenderPlanner.Settings(stress);
        Check(settings["MochizukiUnlockPasses"] == "true" && settings["MochizukiPasses"] == "30" && settings["MochizukiMaxPasses"] == "0" && !settings.ContainsKey("MochizukiLayerMode"), "30 Pass contract incorrect");
        Check(stress.ModeLabel.Contains("压力测试") && stress.Status == "experimental", "Stress test mislabeled highest recommended quality");
        Reject(() => NeuralRenderPlanner.Select("steam:1", "RX 7900 XT", "dx12", null, new("daniel", Passes: 30), now), "Daniel accepted 30 passes");
        Reject(() => NeuralRenderPlanner.Validate(new(ModelScale: double.NaN)), "NaN accepted");
        var rule = new NeuralGameRule("steam:1", "lmxxf", "dxgi.dll", ["rx9000"], ["dx12"], "community", 2, false, true, true);
        var doc = new NeuralRuleDocument(1, "r1", now.AddDays(1), [rule]);
        var matched = NeuralRenderPlanner.Select("steam:1", "RX 9070 XT", "dx12", doc, new(), now);
        Check(matched.Backend == "lmxxf" && matched.Status == "community" && matched.EarlyAnyModuleWrap && matched.AllowEnhancedBarriers && !matched.NrInline, "Remote game rules ignored");
        var unmatched = NeuralRenderPlanner.Select("steam:2", "RX 9070 XT", "dx12", doc, new(), now);
        Check(unmatched.Backend == "daniel" && !unmatched.AllowEnhancedBarriers, "Rules leaked across game IDs");
        var manual = NeuralRenderPlanner.Select("steam:1", "RX 9070 XT", "dx12", doc, new("mochizuki"), now);
        Check(manual.Status != "community" && manual.Status != "verified", "Manual backend inherited another backend's validation");
        Check(NeuralRenderPlanner.Select("steam:1", "RX 9070 XT", "dx12", doc with { ExpiresUtc = now.AddDays(-1) }, new(), now).RuleRevision == "fallback", "Expired compatibility rules used");
        Reject(() => NeuralRules.Validate(doc with { Rules = [rule, rule] }), "Overlapping game rules accepted");
        Reject(() => NeuralRules.Validate(doc with { Rules = [rule with { Backend = "mochizuki", Status = "verified", GpuSeries = ["rx7000"] }] }), "RX7000 Mochizuki labeled verified");
        Reject(() => NeuralRules.Validate(doc with { Rules = [rule with { Proxy = "../../evil.dll" }] }), "Path injection via rules accepted");
        Check(!PresetRules.Allowed(6, new("OptiScaler.ini", "DlssNr", "MochizukiLayerMode", "1")), "Retired key shared");
        Reject(() => PresetRules.Validate(6, [new("OptiScaler.ini", "DlssNr", "NrBackend", "mochizuki"), new("OptiScaler.ini", "DlssNr", "MochizukiPasses", "30")]), "Community stress config without unlock accepted");
        Console.WriteLine($"Neural rendering policy/INI: {count} assertions passed.");
    }
    // Real pinned host/modules; deliberately synthetic lmxxf model data. Exercises
    // only file transactions, never claims a working GPU model or game result.
    public static void RealPackage(string package, string root)
    {
        Directory.CreateDirectory(root); root = Path.GetFullPath(root);
        GameManagement.StorageOverride = Path.Combine(root, "records"); NeuralRenderInstaller.ResourcesOverride = Path.Combine(root, "resources");
        Directory.CreateDirectory(Path.Combine(NeuralRenderInstaller.Resources, "native-game-tiled-assets"));
        File.WriteAllBytes(Path.Combine(NeuralRenderInstaller.Resources, "native-game-tiled-assets/block0-ffn.f16"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(NeuralRenderInstaller.Resources, "native-game-tiled-assets/layout.i32"), [0, 0, 0, 0]);
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game); var exe = Path.Combine(game, "fixture.exe");
        var pe = new byte[512]; pe[0] = 0x4d; pe[1] = 0x5a; BitConverter.GetBytes(0x80).CopyTo(pe, 0x3c);
        pe[0x80] = 0x50; pe[0x81] = 0x45; BitConverter.GetBytes((ushort)0x8664).CopyTo(pe, 0x84); File.WriteAllBytes(exe, pe);
        var ini = Path.Combine(game, "OptiScaler.ini");
        var original = "; original\r\n[XeFG]\r\nInterpolationCount=4\r\n[FrameGen]\r\nEnabled=true\r\n[DlssNr]\r\nEnabled=false\r\nNrBackend=daniel\r\n[User]\r\nKeep=1\r\n";
        File.WriteAllText(ini, original);
        var plan = NeuralRenderPlanner.Select("steam:fixture", "RX 9070 XT", "dx12", null, new("lmxxf", Passes: 2), DateTimeOffset.UtcNow);
        var foreign = Path.Combine(game, "dxgi.dll"); File.WriteAllText(foreign, "foreign ReShade");
        Reject(() => NeuralRenderInstaller.Install(exe, package, plan), "Foreign proxy overwritten");
        Check(File.ReadAllText(foreign) == "foreign ReShade" && File.ReadAllText(ini) == original && GameManagement.Read(exe) == null, "Failed preflight changed files"); File.Delete(foreign);
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Reject(() => NeuralRenderInstaller.Install(exe, package, plan, cancel.Token), "Cancelled install mutated files"); }
        Check(File.ReadAllText(ini) == original && GameManagement.Read(exe) == null, "Cancellation changed game");
        NeuralRenderInstaller.Install(exe, package, plan);
        var record = GameManagement.Read(exe)!;
        Check(record is { Mode: 6, Phase: "installed", Neural: not null } && record.Neural.Backend == "lmxxf" && record.Neural.Passes == 2, "Manifest missing chosen plan");
        Check(File.ReadAllText(ini).Contains("InterpolationCount=4") && File.ReadAllText(ini).Contains("[FrameGen]\r\nEnabled=true"), "Existing FG preferences lost");
        Check(record.Files.All(f => f.After == Core.Hash(Path.Combine(game, f.Name))), "Installed file hash mismatch");
        Check(!File.Exists(Path.Combine(game, "Setup.bat")) && !Directory.Exists(Path.Combine(game, "person-model")) && !File.Exists(Path.Combine(game, "MochizukiNrRuntime.dll")), "Unselected setup/worker/runtime deployed");
        var uploaded = CommunityPresetConfig.Capture(exe);
        Check(uploaded.Settings.Any(s => s.Key == "NrBackend" && s.Value == "lmxxf"), "New scheme cannot share actual settings");
        var later = ManagedIni.Set(File.ReadAllText(ini), "XeFG", "InterpolationCount", "6"); File.WriteAllText(ini, later);
        GameManagement.Restore(exe);
        Check(ManagedIni.Get(File.ReadAllText(ini), "XeFG", "InterpolationCount") == "6" && ManagedIni.Get(File.ReadAllText(ini), "DlssNr", "Enabled") == "false", "Restore erased later unrelated settings");
        Check(!File.Exists(foreign) && !Directory.Exists(Path.Combine(game, "OptiScaler")) && !Directory.Exists(Path.Combine(game, "lmxxf-modules")), "Restore left installed component directories");
        File.WriteAllText(ini, original); NeuralRenderInstaller.Install(exe, package, plan);
        var update = plan with { Passes = 3, Options = plan.Options with { Passes = 3 }, Status = "unverified" };
        NeuralRenderInstaller.UpdateSettings(exe, update);
        Check(GameManagement.Read(exe)!.Neural!.Passes == 3 && ManagedIni.Get(File.ReadAllText(ini), "DlssNr", "Passes") == "3", "Advanced settings/manifest diverged");
        GameManagement.Restore(exe); Check(File.ReadAllText(ini) == original, "Advanced settings prevented exact original restore");
        NeuralRenderInstaller.Install(exe, package, plan);
        record = GameManagement.Read(exe)!; GameManagement.Save(record with { Phase = "installing" });
        File.Delete(Path.Combine(game, "LmxxfNrRuntime.dll")); // interrupted install, some intended writes absent
        GameManagement.Restore(exe); Check(File.ReadAllText(ini) == original && GameManagement.Read(exe)!.Phase == "restored", "Crash intent could not recover");
        NeuralRenderInstaller.Install(exe, package, plan);
        File.WriteAllText(foreign, "another tool changed proxy");
        Reject(() => GameManagement.Restore(exe), "Externally changed runtime deleted");
        Check(File.Exists(Path.Combine(game, "LmxxfNrRuntime.dll")), "Restore mutated files before detecting conflict");
        File.Copy(Path.Combine(package, "OptiScaler.dll"), foreign, true); GameManagement.Restore(exe);
        Check(File.ReadAllText(ini) == original, "Final restore mismatch");
        using (var cancel = new CancellationTokenSource())
            Reject(() => NeuralRenderInstaller.Install(exe, package, plan, cancel.Token, new InlineProgress(_ => cancel.Cancel())), "Mid-install cancellation not observed");
        Check(GameManagement.Read(exe)!.Phase == "restored" && !File.Exists(foreign) && File.ReadAllText(ini) == original &&
            !Directory.Exists(Path.Combine(game, "OptiScaler")), "Cancelled install failed automatic rollback");
        var model = Path.Combine(NeuralRenderInstaller.Resources, "native-game-tiled-assets/block0-ffn.f16");
        var changed = false;
        Reject(() => NeuralRenderInstaller.Install(exe, package, plan, default, new InlineProgress(_ => {
            if (!changed) { changed = true; File.WriteAllBytes(model, [5, 6, 7, 8]); }
        })), "Source changed after preflight accepted");
        Check(GameManagement.Read(exe)!.Phase == "restored" && !File.Exists(foreign) && File.ReadAllText(ini) == original, "Write failure did not automatically rollback");
        Console.WriteLine($"Neural rendering real-package transactions: {count} assertions; synthetic models, no GPU/game execution.");
    }
}
