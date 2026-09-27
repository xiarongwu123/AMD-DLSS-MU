using AmdNrAssistant;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "amd-management-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
GameManagement.StorageOverride = Path.Combine(root, "records");
var count = 0;
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
Assert(Core.ParseRelease(ReleaseJson("v0.3.1", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize)).Tag == "v0.3.1", "accept pinned upstream v0.3.1");
Reject(() => Core.ParseRelease(ReleaseJson("v0.3.0", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize)), "reject old runtime release");
Reject(() => Core.ParseRelease(ReleaseJson("v0.3.1", new string('0', 64), Core.ReviewedInstallerSize)), "reject replaced upstream asset");
Reject(() => Core.ParseRelease(ReleaseJson("v0.3.1", Core.ReviewedInstallerSha256, Core.ReviewedInstallerSize + 1)), "reject changed upstream size");
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
    Assert(config.RootElement.GetProperty("scalingModes")[0].GetProperty("effects")[0].GetProperty("scalingType").GetInt32() == 1, "Magpie default scales to fit");
}
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
