using AmdNrAssistant;
using Mu.Compatibility;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 2 && args[0] == "--live-magpie")
{
    await DownloadResumeTests.RunLive(args[1]);
    return;
}

if (args is ["verify-combined", var baseArchive, var overlayArchive, var destination])
{
    CombinedRenderInstaller.ExtractVerified(baseArchive, overlayArchive, destination);
    var configured = CombinedRenderInstaller.Configure(File.ReadAllText(Path.Combine(destination, "OptiScaler.ini")), "game.exe");
    if (!configured.Contains("FGOutput=xefg") || !configured.Contains("Quality=1")) throw new Exception("Invalid combined configuration");
    Console.WriteLine("Combined archives and configuration verified.");
    return;
}
if (args is ["verify-combined-install", var sourcePackage, var gameDirectory])
{
    Directory.CreateDirectory(gameDirectory);
    GameManagement.StorageOverride = Path.Combine(gameDirectory, "records");
    var gameExe = Path.Combine(gameDirectory, "fixture.exe");
    var pe = new byte[512]; pe[0] = 0x4d; pe[1] = 0x5a;
    BitConverter.GetBytes(0x80).CopyTo(pe, 0x3c);
    pe[0x80] = 0x50; pe[0x81] = 0x45;
    BitConverter.GetBytes((ushort)0x8664).CopyTo(pe, 0x84);
    File.WriteAllBytes(gameExe, pe);
    CombinedRenderInstaller.Install(gameExe, sourcePackage);
    if (GameManagement.Read(gameExe) is not { Mode: 4, Phase: "installed" } ||
        !File.Exists(Path.Combine(gameDirectory, "OptiScaler", "libxess_fg.dll")) ||
        !File.Exists(Path.Combine(gameDirectory, Core.DllName)) ||
        Core.Hash(Path.Combine(gameDirectory, Core.DllName)) != Core.BundledDllSha256)
        throw new Exception("Combined install was not tracked");
    var checks = SmartRenderDiagnostics.Check(gameExe);
    if (checks.Any(item => (item.Title is "神经渲染模型组件" or "XeFG 组件") && item.Problem) ||
        checks.Any(item => item.Title == "FSR 帧生成组件"))
        throw new Exception("Combined diagnosis reported missing or unrelated components");
    File.Delete(Path.Combine(gameDirectory, Core.DllName));
    if (!SmartRenderDiagnostics.Check(gameExe).Any(item => item.Title == "神经渲染模型组件" && item.Problem))
        throw new Exception("Combined diagnosis missed the absent model DLL");
    File.Copy(Path.Combine(sourcePackage, Core.DllName), Path.Combine(gameDirectory, Core.DllName));
    var pass = Path.Combine(gameDirectory, "dlssnr_amd_pass1.dll");
    File.WriteAllText(pass, "tampered");
    if (!SmartRenderDiagnostics.Check(gameExe).Any(item => item.Title == "安装文件完整性" && item.Problem && item.Detail.Contains("dlssnr_amd_pass1.dll")))
        throw new Exception("Combined diagnosis missed a modified runtime file");
    File.Copy(Path.Combine(sourcePackage, "version.dll"), pass, true);
    GameManagement.Restore(gameExe);
    if (File.Exists(Path.Combine(gameDirectory, "dxgi.dll")) ||
        File.Exists(Path.Combine(gameDirectory, "OptiScaler", "libxess_fg.dll")) ||
        File.Exists(Path.Combine(gameDirectory, Core.DllName)))
        throw new Exception("Combined restore did not remove installed files");
    var existingDirectory = gameDirectory + "-existing";
    Directory.CreateDirectory(existingDirectory);
    var existingExe = Path.Combine(existingDirectory, "fixture.exe");
    File.Copy(gameExe, existingExe);
    File.Copy(Path.Combine(sourcePackage, Core.DllName), Path.Combine(existingDirectory, Core.DllName));
    CombinedRenderInstaller.Install(existingExe, sourcePackage);
    GameManagement.Restore(existingExe);
    if (!File.Exists(Path.Combine(existingDirectory, Core.DllName)) ||
        Core.Hash(Path.Combine(existingDirectory, Core.DllName)) != Core.BundledDllSha256)
        throw new Exception("Combined restore changed the game's existing model DLL");
    var missingSource = Path.Combine(sourcePackage, "version.dll");
    var heldSource = missingSource + ".audit-hold";
    File.Move(missingSource, heldSource);
    try
    {
        var rejected = false;
        try { CombinedRenderInstaller.Install(gameExe, sourcePackage); }
        catch (IOException) { rejected = true; }
        if (!rejected || GameManagement.Read(gameExe)?.Phase != "restored")
            throw new Exception("Incomplete package was not rejected before installation");
    }
    finally { File.Move(heldSource, missingSource); }
    Console.WriteLine("Combined install and restore verified.");
    return;
}

var root = Path.Combine(Path.GetTempPath(), "amd-management-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
GameManagement.StorageOverride = Path.Combine(root, "records");
var count = 0;
var assettoDirectory = Path.Combine(root, "assettocorsa");
Directory.CreateDirectory(assettoDirectory);
var assettoLauncher = Path.Combine(assettoDirectory, "AssettoCorsa.exe");
var assettoGame = Path.Combine(assettoDirectory, "acs.exe");
File.WriteAllText(assettoLauncher, "launcher fixture");
Assert(GameManagement.ResolveGameExecutable(assettoLauncher) == assettoLauncher,
    "Assetto Corsa launcher stays selected when the x64 game is absent");
var assettoPe = new byte[256];
assettoPe[0] = 0x4d; assettoPe[1] = 0x5a;
BitConverter.GetBytes(0x80).CopyTo(assettoPe, 0x3c);
assettoPe[0x80] = 0x50; assettoPe[0x81] = 0x45;
BitConverter.GetBytes((ushort)0x8664).CopyTo(assettoPe, 0x84);
File.WriteAllBytes(assettoGame, assettoPe);
Assert(GameManagement.ResolveGameExecutable(assettoLauncher) == assettoGame,
    "Assetto Corsa launcher resolves to its x64 game executable");
File.Delete(assettoLauncher);
Assert(GameManagement.ResolveGameExecutable(assettoLauncher) == assettoLauncher,
    "unrelated folders containing acs.exe do not become Assetto Corsa games");
var invalidGame = Game("invalid-game");
var invalidCheck = GameManagement.Check(invalidGame, true, false, 2);
Assert(invalidCheck.Blocked && invalidCheck.BlockingReasons.Any(reason => reason.Contains("Windows 程序")),
    "smart check exposes the invalid game executable instead of a generic failure");
File.WriteAllText(Path.Combine(Path.GetDirectoryName(invalidGame)!, "dxgi.dll"), "existing mod");
var conflictCheck = GameManagement.Check(invalidGame, true, false, 2);
Assert(conflictCheck.BlockingReasons.Length == 2 &&
    conflictCheck.BlockingReasons.Any(reason => reason.Contains("旧安装或其他插件")),
    "smart check preserves multiple exact blocking reasons");
var capableGpu = new CompatibilityGpu("AMD Radeon RX 9070 XT", "AMD", 16 * 1024, "RDNA4", "known");
Assert(SmartRenderAdvisor.Recommend(capableGpu, true).QualityMode == 0,
    "tested GPU family with enough VRAM can receive tentative neural quality recommendation");
Assert(SmartRenderAdvisor.Recommend(capableGpu, true).PerformanceMode == 4,
    "performance goal selects combined DLSS 5 and XeFG when neural prerequisites pass");
var experimentalGpu = new CompatibilityGpu("AMD Radeon RX 7800 XT", "AMD", 16 * 1024, "RDNA3", "known");
Assert(SmartRenderAdvisor.Recommend(experimentalGpu, true).QualityMode == 0,
    "RX 7000 can receive the updated neural quality route");
Assert(SmartRenderAdvisor.Recommend(experimentalGpu, true).PerformanceMode == 4,
    "RX 7000 can receive the combined neural performance route");
foreach (var name in new[] { "AMD Radeon RX 7900 XT", "AMD Radeon RX 7900 XTX", "AMD Radeon RX 7900 GRE" })
{
    var unreportedVram = new CompatibilityGpu(name, "AMD", null, "unknown", "known");
    var recommendation = SmartRenderAdvisor.Recommend(unreportedVram, true);
    Assert(recommendation.Availability == SmartRenderAvailability.Ready &&
        recommendation.QualityMode == 0 && recommendation.PerformanceMode == 4 &&
        recommendation.Notices.Any(n => n.Contains("显存未读到")),
        name + " is not blocked solely because dxdiag omitted dedicated memory");
}
var measuredLow7900 = new CompatibilityGpu("AMD Radeon RX 7900 XTX", "AMD", 4096, "unknown", "known");
Assert(SmartRenderAdvisor.Recommend(measuredLow7900, true).Availability == SmartRenderAvailability.HardwareUnsupported,
    "a measured low memory value is not overwritten by the RX 7900 model fallback");
var unknownMemoryGpu = new CompatibilityGpu("AMD Radeon RX 7700 XT", "AMD", null, "unknown", "known");
Assert(SmartRenderAdvisor.Recommend(unknownMemoryGpu, true).Availability == SmartRenderAvailability.HardwareUnconfirmed,
    "unverified GPU models still require measured memory");
Assert(SmartRenderAdvisor.Recommend(capableGpu, false).QualityMode == 2,
    "missing neural prerequisites fall back to standard OptiScaler");
Assert(SmartRenderAdvisor.Recommend(capableGpu, false).PerformanceMode == 2,
    "performance goal falls back when neural prerequisites fail");
Assert(SmartRenderAdvisor.Recommend(capableGpu, false).Availability == SmartRenderAvailability.RequirementsMissing,
    "missing HIP or game prerequisites are not mislabeled as unsupported hardware");
var rdna2 = new CompatibilityGpu("AMD Radeon RX 6750 GRE 10GB", "AMD", 10 * 1024, "RDNA2", "known");
Assert(SmartRenderAdvisor.Recommend(rdna2, true).QualityMode == 0 &&
    SmartRenderAdvisor.Recommend(rdna2, true).PerformanceMode == 4, "RX 6750 GRE gets both v0.6.0 routes when prerequisites pass");
foreach (var mode in new[] { 0, 4 })
{
    Assert(GameManagement.NeuralGpuBlockReason(new[] { "AMD Radeon RX 6750 GRE 10GB | Driver 32.0.21037.1004" }, mode) == null,
        $"RX 6750 GRE is not rejected by mode {mode}");
    Assert(GameManagement.NeuralGpuBlockReason(new[] { "AMD Radeon RX 5700 XT" }, mode) != null,
        $"RX 5000 remains outside the neural support range in mode {mode}");
}
Assert(SmartRenderAdvisor.Recommend(rdna2, false).QualityMode == 2, "RX 6000 without prerequisites keeps standard fallback");
var combinedTemplate = "[ProcessFilter]\nTargetProcessName=auto\n[Menu]\nOverlayMenu=auto\n[DlssNr]\nEnabled=false\nNrBackend=lmxxf\nQuality=0\n[FrameGen]\nExternal=false\nEnabled=false\nFGInput=auto\nFGOutput=auto\n[XeFG]\nInterpolationCount=auto";
var combinedIni = CombinedRenderInstaller.Configure(combinedTemplate, "game.exe");
Assert(combinedIni.Contains("FGInput=upscaler") && combinedIni.Contains("FGOutput=xefg") &&
    combinedIni.Contains("InterpolationCount=1") && combinedIni.Contains("NrBackend=daniel") &&
    combinedIni.Contains("Quality=1"), "combined route configures DLSS neural and conservative XeFG 2X");
Assert(SmartRenderAdvisor.Recommend(null, false).QualityMode == 2,
    "unknown GPU receives conservative standard recommendation");
var integrated = new CompatibilityGpu("AMD Radeon(TM) Graphics", "AMD", 512, "unknown", "known");
Assert(SmartRenderAdvisor.SelectGpu([integrated, capableGpu]) == capableGpu,
    "recommendation chooses discrete RX card when dxdiag also lists integrated graphics");
var integratedRecommendation = SmartRenderAdvisor.Recommend(integrated, true, true);
Assert(SmartRenderAdvisor.RecommendedMode(SmartRenderGoal.Quality, integratedRecommendation) == 2,
    "integrated graphics with 0.5 GB VRAM selects standard route");
Assert(integratedRecommendation.Availability == SmartRenderAvailability.HardwareUnsupported &&
    integratedRecommendation.QualityReason.Contains("不支持开启 DLSS 5"),
    "unsupported detected graphics explicitly report that DLSS 5 cannot be enabled");
Assert(SmartRenderAdvisor.RecommendedMode(SmartRenderGoal.Quality,
    SmartRenderAdvisor.Recommend(capableGpu, true, true)) == 0 &&
    SmartRenderAdvisor.RecommendedMode(SmartRenderGoal.Performance,
    SmartRenderAdvisor.Recommend(capableGpu, true, true)) == 4,
    "quality and performance goals select different install routes");
var smartChoice = new SmartRenderSelection();
var performanceOnly = SmartRenderAdvisor.Recommend(capableGpu, false, true);
Assert(performanceOnly.QualityMode == 2 && performanceOnly.PerformanceMode == 4,
    "unavailable quality route does not silently reuse the performance route");
smartChoice.SetRecommendation(SmartRenderAdvisor.Recommend(capableGpu, true, true), SmartRenderGoal.Quality);
Assert(smartChoice.Mode == 0 && !smartChoice.ShowSchemeList && smartChoice.CanConfigure,
    "quality automatically selects mode 1 and hides the scheme list");
Assert(!smartChoice.SelectMode(4) && smartChoice.Mode == 0,
    "manual route selection cannot override quality mode");
Assert(smartChoice.SelectGoal(SmartRenderGoal.Performance) && smartChoice.Mode == 4 && !smartChoice.ShowSchemeList,
    "performance automatically selects XeFG and hides the scheme list");
Assert(!smartChoice.SelectMode(2) && smartChoice.Mode == 4,
    "manual route selection cannot override performance mode");
Assert(smartChoice.SelectGoal(SmartRenderGoal.Custom) && smartChoice.ShowSchemeList && smartChoice.SelectMode(2),
    "only custom mode exposes and enables manual route selection");
Assert(smartChoice.SelectGoal(SmartRenderGoal.Quality) && smartChoice.Mode == 0 && !smartChoice.ShowSchemeList,
    "leaving custom restores the chosen goal's automatic route");
smartChoice.SetRecommendation(integratedRecommendation, SmartRenderGoal.Quality);
Assert(!smartChoice.CanConfigure && !smartChoice.ShowSchemeList && !smartChoice.SelectGoal(SmartRenderGoal.Performance),
    "unsupported GPU never silently auto-configures standard OptiScaler as DLSS 5");
Assert(smartChoice.SelectGoal(SmartRenderGoal.Custom) && smartChoice.Mode == 2 && !smartChoice.SelectMode(0),
    "unsupported GPU requires explicit custom selection for the standard route");
const string fgIni = "[Log]\nLogToFile=auto\nLogLevel=auto\n[Menu]\nOverlayMenu=auto\nShortcutKey=auto\n[ProcessFilter]\nTargetProcessName=auto\n[FrameGen]\nEnabled=auto\nFGInput=auto\nFGOutput=auto";
var safeFg = OptiInstaller.Configure(fgIni, "game.exe", false);
Assert(safeFg.Contains("Enabled=false") && safeFg.Contains("FGInput=nofg") && safeFg.Contains("FGOutput=nofg"),
    "automatic profiles leave unverified FG disabled");
var customFg = OptiInstaller.Configure(fgIni, "game.exe", false, "dlssg");
Assert(customFg.Contains("Enabled=true") && customFg.Contains("FGInput=dlssg") && customFg.Contains("FGOutput=fsrfg"),
    "custom native DLSSG writes a real FSR FG route");
Reject(() => OptiInstaller.Configure(fgIni, "game.exe", false, "unknown"), "unknown FG routes are rejected");
Reject(() => OptiInstaller.Configure(fgIni, "game.exe", true, "dlssg"), "DX12 FG cannot be selected with Vulkan injection");
Assert(LibraryPaging.PageCount(0) == 1 && LibraryPaging.ClampPage(5, 0) == 0, "empty library has stable first page");
Assert(LibraryPaging.PageCount(8) == 1 && LibraryPaging.PageCount(9) == 2, "ninth game starts a new eight-game page");
Assert(LibraryPaging.PageCount(16) == 2 && LibraryPaging.PageCount(17) == 3, "exact and partial pages counted correctly");
Assert(LibraryPaging.ClampPage(2, 3) == 0, "filtering to three games resets out-of-range page");
Assert(LibraryPaging.ClampPage(-1, 24) == 0, "previous page cannot go below zero");
await DownloadTests.Run(root, Assert);
InstallerProtocolTests.Run(Assert);
void Assert(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
void Reject(Action act, string name) { try { act(); } catch (IOException) { Assert(true, name); return; } throw new Exception("Expected rejection: " + name); }
string Game(string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); var e = Path.Combine(d, name + ".exe"); File.WriteAllText(e, "fixture"); return e; }
var a = Game("a" + Guid.NewGuid().ToString("N")); var b = Game("b" + Guid.NewGuid().ToString("N")); var dir = Path.GetDirectoryName(a)!;
string ReleaseJson(string tag, string digest, long size) => JsonSerializer.Serialize(new {
    draft = false, prerelease = false, tag_name = tag,
    assets = new[] { new { name = Core.InstallerName,
        browser_download_url = Core.Repository + "/releases/download/" + tag + "/" + Core.InstallerName,
        digest = "sha256:" + digest, size } }
});
Assert(Core.ParseRelease(ReleaseJson("v0.6.0", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize)).Tag == "v0.6.0", "accept pinned upstream v0.6.0");
Reject(() => Core.ParseRelease(ReleaseJson("v0.3.0", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize)), "reject old runtime release");
Reject(() => Core.ParseRelease(ReleaseJson("v0.3.1", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize)), "reject legacy console installer release");
Reject(() => Core.ParseRelease(ReleaseJson("v0.6.0", new string('0', 64), Core.ReviewedInstallerSize)), "reject replaced upstream asset");
Reject(() => Core.ParseRelease(ReleaseJson("v0.6.0", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize + 1)), "reject changed upstream size");
Reject(() => Core.ParseRelease(ReleaseJson("v0.4.0", "2d37453e918a1c5487314b3ae7c088b624ac0be1b82469427ec04b481a845ded", 13183488)), "obsolete 0.4.0 release is rejected");
Assert(!GameManagement.IsUnsupportedAmdNeuralGpuOnly(new[] { "AMD Radeon RX 6750 GRE 10GB | Driver 32.0.21037.1004" }), "RX 6750 GRE is supported by the runtime gate");
Assert(GameManagement.HasRx6000(new[] { "AMD Radeon RX 6750 GRE 10GB" }), "RX 6750 GRE requires HIP 7.2 notice");
Assert(HipRuntime.IsVersion72("7.2.0.0") && HipRuntime.IsVersion72("AMD HIP 7.2 runtime"), "detect HIP 7.2 version");
Assert(!HipRuntime.IsVersion72("7.1.1") && !HipRuntime.IsVersion72("7.20.0") && !HipRuntime.IsVersion72(null), "reject other HIP versions");
Assert(HipRuntime.CreateInstallStartInfo("C:/Downloads/" + HipRuntime.InstallerName).Verb == "runas", "AMD installer requests UAC");
Assert(GameManagement.IsUnsupportedAmdNeuralGpuOnly(new[] { "AMD Radeon RX 5700 XT" }), "RX 5000 remains unsupported");
Assert(!GameManagement.IsUnsupportedAmdNeuralGpuOnly(new[] { "AMD Radeon RX 5700 XT", "AMD Radeon RX 7800 XT" }), "older secondary adapter does not block supported GPU");
Assert(Core.InstallerMirror.Contains("/v0.6.0/") && Core.InstallerApi.EndsWith("607036746"), "official mirror and API asset use 0.6.0");
var guiStart = UpstreamInstaller.CreateStartInfo("installer.exe", a);
Assert(!guiStart.CreateNoWindow && !guiStart.RedirectStandardInput && !guiStart.RedirectStandardOutput && !guiStart.RedirectStandardError && guiStart.ArgumentList.Count == 0 && guiStart.Arguments == "", "graphical installer is visible and receives no guessed CLI flags or automatic confirmations");
Assert(guiStart.WorkingDirectory == dir, "graphical installer starts in selected game directory");
GameLibrary.StorageOverride = Path.Combine(root, "library", "manual-games.json");
GameLibrary.AddManual(a);
GameLibrary.AddManual(a);
GameLibrary.AddManual(b);
Assert(GameLibrary.ReadManual().Count == 2, "manual library persists and deduplicates additions");
Assert(GameLibrary.ReadManual().Contains(a), "manual games available after reload");
var manifest = Path.Combine(root, "custom.item");
File.WriteAllText(manifest, JsonSerializer.Serialize(new { DisplayName = "Custom Epic Game", InstallLocation = dir, LaunchExecutable = Path.GetFileName(a) }));
var epic = GameLibrary.ReadEpicManifest(manifest);
Assert(epic?.Exe == a && epic.Title == "Custom Epic Game", "Epic custom install location and launch executable discovered");
File.WriteAllText(manifest, JsonSerializer.Serialize(new { InstallLocation = dir, LaunchExecutable = "../" + Path.GetFileName(Path.GetDirectoryName(b)) + "/" + Path.GetFileName(b) }));
Assert(GameLibrary.ReadEpicManifest(manifest)?.Exe == null, "Epic executable cannot escape install directory");
File.WriteAllText(manifest, "{broken");
Assert(GameLibrary.ReadEpicManifest(manifest) == null, "malformed Epic manifest skipped");
File.WriteAllText(manifest, JsonSerializer.Serialize(new { InstallLocation = Path.Combine(root, "missing") }));
Assert(GameLibrary.ReadEpicManifest(manifest) == null, "missing Epic install skipped");
File.WriteAllText(GameLibrary.StorageOverride, "{broken");
Reject(() => GameLibrary.AddManual(a), "corrupt manual library is not overwritten");
Assert(File.ReadAllText(GameLibrary.StorageOverride) == "{broken", "corrupt library preserved for recovery");
var proxy = Path.Combine(dir, "version.dll"); File.WriteAllText(proxy, "original");
GameManagement.Begin(a, 0); File.WriteAllText(proxy, "installed"); File.WriteAllText(Path.Combine(dir, "dlssnr_on_amd.ini"), "new"); GameManagement.Finish(a, true);
Assert(GameManagement.Read(a)?.Mode == 0 && GameManagement.Read(a)?.Phase == "installed", "persistent record");
Assert(GameManagement.Read(b) == null, "per-game isolation");
Assert(GameManagement.ModeName(2).Contains("模式二"), "OptiScaler retains ID 2 but displays Mode 2");
Assert(GameManagement.ModeName(1).Contains("Retired"), "legacy Pre-SR is not relabeled as OptiScaler");
File.WriteAllText(proxy, "external edit"); Reject(() => GameManagement.Restore(a), "modified target blocks complete restore");
Assert(File.Exists(Path.Combine(dir, "dlssnr_on_amd.ini")), "preflight prevents partial removal");
File.WriteAllText(proxy, "installed"); var backup = Path.Combine(GameManagement.State(a), "version.dll.backup"); File.WriteAllText(backup, "corrupt");
Reject(() => GameManagement.Restore(a), "corrupt backup rejected"); Assert(File.ReadAllText(proxy) == "installed", "corrupt backup never copied");
File.WriteAllText(backup, "original"); GameManagement.Restore(a);
Assert(File.ReadAllText(proxy) == "original" && !File.Exists(Path.Combine(dir, "dlssnr_on_amd.ini")), "restore originals and remove additions");
GameManagement.Restore(a); Assert(GameManagement.Read(a)?.Phase == "restored", "idempotent restore");
GameManagement.Begin(b, 1); Reject(() => GameManagement.Restore(b), "interrupted external install fails closed");
var record = GameManagement.Read(a)!;
File.WriteAllText(Path.Combine(GameManagement.State(a), "record.json"), JsonSerializer.Serialize(record with { Files = new() { new("../outside.dll", "", "") } }));
Reject(() => GameManagement.Restore(a), "manifest traversal rejected");
Console.WriteLine($"{count} assertions passed. Fixtures retained at {root}");
Assert(!GameManagement.HasCompletedFrames("frame 0 processed (0 skipped)"), "zero frames not success");
Assert(!GameManagement.HasCompletedFrames("network job 10ms; Enabled=1"), "timing or enabled flag not success");
Assert(GameManagement.HasCompletedFrames("frame 240 processed (2 skipped)"), "documented add-on completed frame format");
var c = Game("legacy" + Guid.NewGuid().ToString("N")); var cd = Path.GetDirectoryName(c)!;
File.WriteAllText(Path.Combine(cd, "winmm.dll"), "unknown plugin");
File.WriteAllText(Path.Combine(cd, "nvngx_dlss.dll"), "native game DLSS");
File.WriteAllText(Path.Combine(cd, "d3d12.dll"), "game component");
var snapshot = GameManagement.LegacyCandidates(c);
Assert(snapshot.Count == 1 && snapshot.ContainsKey("winmm.dll"), "legacy cleanup excludes native DLSS and D3D12");
var wrong = new Dictionary<string,string>(snapshot) { ["../outside.dll"] = "bad" };
Reject(() => GameManagement.QuarantineLegacy(c, wrong), "unapproved path rejected");
File.WriteAllText(Path.Combine(cd, "winmm.dll"), "changed");
Reject(() => GameManagement.QuarantineLegacy(c, snapshot), "changed legacy snapshot blocks cleanup");
Assert(File.Exists(Path.Combine(cd, "winmm.dll")), "failed legacy preflight leaves files intact");
var archive = GameManagement.QuarantineLegacy(c, GameManagement.LegacyCandidates(c));
Assert(!File.Exists(Path.Combine(cd, "winmm.dll")) && File.ReadAllText(Path.Combine(archive, "winmm.dll")) == "changed", "unknown proxy recoverably quarantined");
Assert(File.Exists(Path.Combine(cd, "nvngx_dlss.dll")) && File.Exists(Path.Combine(archive, "manifest.json")), "native game file and recovery manifest preserved");
var d = Game("settings" + Guid.NewGuid().ToString("N")); var ini = Path.Combine(Path.GetDirectoryName(d)!, "dlssnr_on_amd.ini");
GameManagement.Begin(d, 0); File.WriteAllText(ini, "installed"); GameManagement.Finish(d, true);
File.WriteAllText(ini, "user changed settings");
GameManagement.Restore(d, true);
Assert(!File.Exists(ini) && Directory.EnumerateFiles(GameManagement.State(d), "*.ini", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "user changed settings"), "edited settings archived then removed");
GameManagement.Restore(d, true); Assert(GameManagement.Read(d)?.Phase == "restored", "edited settings restore is repeatable");
Console.WriteLine($"Final: {count} assertions passed.");
var eGame = Game("nested" + Guid.NewGuid().ToString("N")); var ed = Path.GetDirectoryName(eGame)!;
GameManagement.Begin(eGame, 2, "OptiScaler 0.9.4");
Directory.CreateDirectory(Path.Combine(ed, "D3D12_Optiscaler"));
var nested = Path.Combine(ed, "D3D12_Optiscaler", "D3D12Core.dll"); File.WriteAllText(nested, "owned");
GameManagement.Finish(eGame, true);
var addon = Path.Combine(ed, "AMD-DLSS-MU.addon64"); File.WriteAllText(addon, "overlay"); GameManagement.RegisterAddon(eGame, "AMD-DLSS-MU.addon64");
Assert(GameManagement.Read(eGame)!.Files.Any(f => f.Name.EndsWith("D3D12Core.dll")), "adding overlay preserves existing manifest");
GameManagement.Restore(eGame); Assert(!File.Exists(nested) && !File.Exists(addon), "restore removes nested payload and addon");
Assert(!GameManagement.IsTracked("Licenses/../../outside.txt") && !GameManagement.IsTracked("Licenses\\..\\outside.txt"), "nested manifest traversal rejected on both separators");
var before = "; keep\r\n[Menu]\r\nScale=auto\r\n[Other]\r\nScale=2\r\n";
var after = OptiInstaller.SetIni(before, "Menu", "Scale", "1.2");
Assert(after.Contains("; keep\r\n") && after.Contains("Scale=1.2") && after.Contains("[Other]\r\nScale=2"), "INI writes only intended section");
Reject(() => OptiInstaller.SetIni("[Menu]\nScale=1\nScale=2", "Menu", "Scale", "1"), "duplicate INI keys rejected");
Reject(() => OptiInstaller.SetIni("[Menu]\nOther=1", "Menu", "Scale", "1"), "unknown config schema rejected");
var configPath = Path.Combine(ed, "OptiScaler.ini"); File.WriteAllText(configPath, before);
LivePanel.SaveConfig(eGame, configPath, before, after);
Assert(File.ReadAllText(configPath) == after && Directory.EnumerateFiles(GameManagement.State(eGame), "OptiScaler.ini", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == before), "config saved with original backup");
Reject(() => LivePanel.SaveConfig(eGame, configPath, before, "stale"), "concurrent config edits rejected");
var redacted = Diagnostics.Redact("C:\\Users\\secret\\Game\\test.dll\ntoken=abcdef\nemail=a@example.com", eGame);
Assert(!redacted.Contains("secret") && !redacted.Contains("abcdef") && !redacted.Contains("a@example.com"), "diagnostic redacts paths credentials and email");
Diagnostics.Record(eGame, "download", "failed", "Timeout before installation");
var report = JsonDocument.Parse(Diagnostics.Export(eGame));
Assert(report.RootElement.GetProperty("Activity").GetProperty("Text").GetString()!.Contains("Timeout before installation"), "download failures survive before installation records");
Assert(report.RootElement.GetProperty("Logs").EnumerateArray().All(l => l.GetProperty("Status").GetString() == "missing"), "missing logs explicit in export");
var log = Path.Combine(ed, "OptiScaler.log"); File.WriteAllText(log, new string('a', 150000));
var excerpt = Diagnostics.ReadTail(ed, "OptiScaler.log", eGame);
Assert(excerpt.Status == "tail-truncated" && excerpt.Text.Length <= 65536, "large logs bounded");
Console.WriteLine($"Expanded: {count} assertions passed.");
var magpieRoot = Path.Combine(root, "magpie-test"); Directory.CreateDirectory(magpieRoot);
foreach (var invalid in new[] { "../escape.dll", "folder/../../escape.dll", "/absolute.dll", "C:\\evil.dll", "safe.dll:stream", "folder./evil.dll" })
    Reject(() => MagpieIntegration.SafeEntryPath(magpieRoot, invalid), "Magpie rejects unsafe entry " + invalid);
Assert(MagpieIntegration.SafeEntryPath(magpieRoot, "effects/Lanczos.hlsl") == Path.Combine(magpieRoot, "effects", "Lanczos.hlsl"), "Magpie accepts nested effects");
using (var config = JsonDocument.Parse(MagpieIntegration.ConfigJson))
{
    Assert(config.RootElement.GetProperty("shortcuts").GetProperty("scale").GetInt32() == (0x400 | 0x800 | 65), "Magpie shortcut is Alt Shift A");
    var mode = config.RootElement.GetProperty("scalingModes")[0];
    Assert(mode.GetProperty("effects")[0].GetProperty("name").GetString() == MagpieIntegration.DefaultEffect,
        "Magpie defaults to real DLSSNR AI Filter effect");
    Assert(!mode.GetProperty("effects")[0].TryGetProperty("scale", out _) &&
        config.RootElement.GetProperty("profiles")[0].GetProperty("scalingMode").GetInt32() == 0,
        "Magpie selects AI filter without forced upscaling");
}
var migrationApp = Path.Combine(root, "magpie-migration");
Directory.CreateDirectory(Path.Combine(migrationApp, "effects", "DLSSNR"));
File.WriteAllText(Path.Combine(migrationApp, "effects", "DLSSNR", "DLSSNR_AI_Filter.hlsl"), "fixture");
var migrationConfig = Path.Combine(migrationApp, "config", "v4e", "config.json");
Directory.CreateDirectory(Path.GetDirectoryName(migrationConfig)!);
File.WriteAllText(migrationConfig, """{"language":"en","scalingModes":[{"name":"Lanczos","effects":[{"name":"Lanczos"}]}],"profiles":[{"scalingMode":0},{"name":"custom","scalingMode":0}]}""");
MagpieIntegration.EnsureDefaultEffect(migrationApp, default);
using (var migrated = JsonDocument.Parse(File.ReadAllText(migrationConfig)))
{
    Assert(migrated.RootElement.GetProperty("scalingModes").GetArrayLength() == 2 &&
        migrated.RootElement.GetProperty("profiles")[0].GetProperty("scalingMode").GetInt32() == 1,
        "existing Magpie default migrates to appended AI filter");
    Assert(migrated.RootElement.GetProperty("language").GetString() == "en" &&
        migrated.RootElement.GetProperty("profiles")[1].GetProperty("scalingMode").GetInt32() == 0,
        "migration preserves user settings and custom profiles");
}
Assert(Directory.GetFiles(Path.GetDirectoryName(migrationConfig)!, "*.bak").Length == 1, "migration backs up original config");
var savedConfig = File.ReadAllText(migrationConfig);
MagpieIntegration.EnsureDefaultEffect(migrationApp, default);
Assert(File.ReadAllText(migrationConfig) == savedConfig && Directory.GetFiles(Path.GetDirectoryName(migrationConfig)!, "*.bak").Length == 1,
    "AI filter migration is one-time and idempotent");
var userChoice = JsonNode.Parse(savedConfig)!;
userChoice["profiles"]![0]!["scalingMode"] = 0;
File.WriteAllText(migrationConfig, userChoice.ToJsonString());
MagpieIntegration.EnsureDefaultEffect(migrationApp, default);
Assert(JsonNode.Parse(File.ReadAllText(migrationConfig))!["profiles"]![0]!["scalingMode"]!.GetValue<int>() == 0,
    "later explicit user choice is not reset on every launch");
var missingEffectApp = Path.Combine(root, "magpie-missing-filter");
Directory.CreateDirectory(Path.Combine(missingEffectApp, "config", "v4e"));
var missingEffectConfig = Path.Combine(missingEffectApp, "config", "v4e", "config.json");
File.WriteAllText(missingEffectConfig, MagpieIntegration.ConfigJson);
Reject(() => MagpieIntegration.EnsureDefaultEffect(missingEffectApp, default), "missing AI effect fails visibly instead of falling back to scaling");
Assert(File.ReadAllText(missingEffectConfig) == MagpieIntegration.ConfigJson, "missing AI effect leaves configuration untouched");
var magpieExe = Path.Combine(magpieRoot, "Magpie.exe"); File.WriteAllText(magpieExe, "fixture");
File.WriteAllText(Path.Combine(magpieRoot, "mu-package.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["Magpie.exe"] = Core.Hash(magpieExe) }));
Assert(MagpieIntegration.ValidateInstallation(magpieRoot, default) == magpieExe, "Magpie validates recorded components");
File.WriteAllText(magpieExe, "changed");
Reject(() => MagpieIntegration.ValidateInstallation(magpieRoot, default), "Magpie rejects changed components");
Console.WriteLine($"Magpie integration: {count} assertions passed.");
if (args.Length == 1)
{
    var extracted = Path.Combine(root, "real-opti"); OptiInstaller.ExtractVerified(args[0], extracted);
    Assert(OptiInstaller.Payload.All(n => File.Exists(Path.Combine(extracted, n))), "real pinned archive has all expected payload files");
    var configured = OptiInstaller.Configure(File.ReadAllText(Path.Combine(extracted, "OptiScaler.ini")), eGame, false);
    Assert(configured.Contains("LogToFile=true") && configured.Contains("ShortcutKey=0x2D"), "real upstream config accepts logging and menu settings");
    var realGame = Game("opti" + Guid.NewGuid().ToString("N"));
    using (var pe = new BinaryWriter(File.Open(realGame, FileMode.Create)))
    { pe.Write((ushort)0x5a4d); pe.BaseStream.Position = 0x3c; pe.Write(64); pe.BaseStream.Position = 64; pe.Write(0x4550); pe.Write((ushort)0x8664); pe.BaseStream.SetLength(128); }
    OptiInstaller.Install(realGame, extracted, false);
    var installedDir = Path.GetDirectoryName(realGame)!;
    Assert(GameManagement.Read(realGame)?.Mode == 2 && File.Exists(Path.Combine(installedDir, "dxgi.dll")), "OptiScaler installs with stable storage ID 2 (display Mode 2)");
    GameManagement.Restore(realGame);
    Assert(!File.Exists(Path.Combine(installedDir, "dxgi.dll")) && !File.Exists(Path.Combine(installedDir, "D3D12_Optiscaler", "D3D12Core.dll")), "real package restore removes proxy and nested dependency");
    File.WriteAllText(Path.Combine(installedDir, "dxgi.dll"), "unrelated plugin");
    Reject(() => OptiInstaller.Install(realGame, extracted, false), "real install refuses existing foreign proxy");
    Assert(File.ReadAllText(Path.Combine(installedDir, "dxgi.dll")) == "unrelated plugin", "foreign proxy preserved");
    Console.WriteLine($"All integration assertions: {count}");
}
