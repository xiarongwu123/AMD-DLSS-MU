using AmdNrAssistant;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "amd-management-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
GameManagement.StorageOverride = Path.Combine(root, "records");
var count = 0;
void Assert(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
void Reject(Action act, string name) { try { act(); } catch (IOException) { Assert(true, name); return; } throw new Exception("Expected rejection: " + name); }
string Game(string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); var e = Path.Combine(d, name + ".exe"); File.WriteAllText(e, "fixture"); return e; }
var a = Game("a" + Guid.NewGuid().ToString("N")); var b = Game("b" + Guid.NewGuid().ToString("N")); var dir = Path.GetDirectoryName(a)!;
var proxy = Path.Combine(dir, "version.dll"); File.WriteAllText(proxy, "original");
GameManagement.Begin(a, 0); File.WriteAllText(proxy, "installed"); File.WriteAllText(Path.Combine(dir, "dlssnr_on_amd.ini"), "new"); GameManagement.Finish(a, true);
Assert(GameManagement.Read(a)?.Mode == 0 && GameManagement.Read(a)?.Phase == "installed", "persistent record");
Assert(GameManagement.Read(b) == null, "per-game isolation");
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
