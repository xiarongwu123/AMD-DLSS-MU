using AmdNrAssistant;
using System.Text.Json;
var current = new Version(1,2,0,0);
string Fixture(string tag = "v1.3.0", string? url = null, string? digest = null, bool draft = false, bool prerelease = false, long size = 2048, int copies = 1) => JsonSerializer.Serialize(new {
 tag_name = tag, draft, prerelease, body = "test", assets = Enumerable.Range(0,copies).Select(_ => new {name = AutoUpdate.AssetName, browser_download_url = url ?? AutoUpdate.Repository + "/releases/download/" + tag + "/" + AutoUpdate.AssetName, digest = digest ?? "sha256:" + new string('a',64), size}) });
int passed = 0;
void Assert(bool b) { if (!b) throw new Exception("assertion failed"); passed++; }
void Reject(string json) { try { AutoUpdate.Parse(json,current); } catch (IOException) { passed++; return; } throw new Exception("bad release accepted"); }
Assert(AutoUpdate.Parse(Fixture(),current)?.Tag == "v1.3.0");
Assert(AutoUpdate.Parse(Fixture("v1.2.0"),current) == null);
Assert(AutoUpdate.Parse(Fixture("v1.1.0"),current) == null);
Assert(AutoUpdate.Parse(Fixture(draft:true),current) == null);
Assert(AutoUpdate.Parse(Fixture(prerelease:true),current) == null);
Reject(Fixture(url:"https://example.org/AMD-DLSS-MU.exe"));
Reject(Fixture(digest:"")); Reject(Fixture(digest:"sha256:bad"));
Reject(Fixture(copies:0)); Reject(Fixture(copies:2));
Reject(Fixture(size:0)); Reject(Fixture(size:2L*1024*1024*1024));
Reject(Fixture(tag:"v1.3.0-evil"));
Console.WriteLine($"PASS {passed} update release validation assertions");
