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
Assert(AutoUpdate.Parse(Fixture("v1.4.0"), new Version(2, 0, 0, 0)) == null);
Assert(AutoUpdate.Parse(Fixture("v2.0.1"), new Version(2, 0, 0, 0))?.Tag == "v2.0.1");
Assert(AutoUpdate.Parse(Fixture("v2.0.2"), new Version(2, 0, 1, 0))?.Tag == "v2.0.2");
Assert(AutoUpdate.Parse(Fixture("v2.0.4"), new Version(2, 0, 3, 0))?.Tag == "v2.0.4");
Assert(AutoUpdate.Parse(Fixture("v2.0.5"), new Version(2, 0, 4, 0))?.Tag == "v2.0.5");
Reject(Fixture(url:"https://example.org/AMD-DLSS-MU.exe"));
Reject(Fixture(digest:"")); Reject(Fixture(digest:"sha256:bad"));
Reject(Fixture(copies:0)); Reject(Fixture(copies:2));
Reject(Fixture(size:0)); Reject(Fixture(size:2L*1024*1024*1024));
Reject(Fixture(tag:"v1.3.0-evil"));
string WebsiteFixture(string version = "2.0.6", string? url = null, string hash = "", string channel = "stable", long size = 2048) => JsonSerializer.Serialize(new {
    version, tag = "v" + version, channel, file = AutoUpdate.AssetName, size,
    sha256 = hash == "" ? new string('a', 64) : hash,
    downloadUrl = url ?? AutoUpdate.MirrorUrl("v" + version, hash == "" ? new string('a', 64) : hash), notes = "Website update"
});
var websiteRelease = AutoUpdate.ParseWebsite(WebsiteFixture(), new Version(2, 0, 5))!;
Assert(websiteRelease.Tag == "v2.0.6" && websiteRelease.MirrorUrl!.StartsWith(AutoUpdate.Website + "/updates/"));
Assert(websiteRelease.Url == AutoUpdate.Repository + "/releases/download/v2.0.6/AMD-DLSS-MU.exe");
Assert(AutoUpdate.ParseWebsite(WebsiteFixture(), new Version(2, 0, 6)) == null);
Assert(AutoUpdate.ParseWebsite(WebsiteFixture("2.0.5"), new Version(2, 0, 6)) == null);
foreach (var invalid in new[] { WebsiteFixture(url: "https://example.org/file.exe"), WebsiteFixture(url: AutoUpdate.Website + "/download/file"),
    WebsiteFixture(hash: "bad"), WebsiteFixture(channel: "preview"), WebsiteFixture(size: 0), WebsiteFixture(size: 2L * 1024 * 1024 * 1024) })
{
    try { AutoUpdate.ParseWebsite(invalid, current); throw new Exception("bad website release accepted"); }
    catch (IOException) { passed++; }
}
foreach (var failure in new[] { "none", "http", "timeout", "malformed", "untrusted" })
{
    var urls = new List<string>();
    using var client = new HttpClient(new UpdateHandler(request => {
        var url = request.RequestUri!.AbsoluteUri; urls.Add(url);
        if (url == AutoUpdate.WebsiteManifest)
        {
            if (failure == "timeout") throw new TaskCanceledException("timeout");
            return new(failure == "http" ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK) {
                Content = new StringContent(failure == "malformed" ? "{broken" : failure == "untrusted" ? WebsiteFixture(url: "https://example.org/file.exe") : WebsiteFixture()) };
        }
        return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(Fixture("v2.0.6")) };
    }));
    var found = await AutoUpdate.CheckAsync(client, new Version(2, 0, 5), default);
    Assert(found?.Tag == "v2.0.6");
    Assert(urls.SequenceEqual(failure == "none" ? new[] { AutoUpdate.WebsiteManifest } : new[] { AutoUpdate.WebsiteManifest, AutoUpdate.GithubManifest }));
}
using (var cancellation = new CancellationTokenSource())
using (var client = new HttpClient(new UpdateHandler(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); })))
{
    try { await AutoUpdate.CheckAsync(client, current, cancellation.Token); throw new Exception("cancellation swallowed"); }
    catch (OperationCanceledException) { passed++; }
}
if (args.Length is 1 or 2)
{
    Core.CheckPe(args[0], false);
    // FileVersionInfo on macOS returns empty for native bundled Windows hosts.
    // Read the actual RT_VERSION resource instead of treating that as version zero.
    var version = ReadPeVersion(args[0]);
    Assert(version == (args.Length == 2 ? Version.Parse(args[1]) : new Version(2, 0, 6, 0)));
    Assert(new FileInfo(args[0]).Length > 100_000_000);
    Console.WriteLine("Verified Windows x64 release v" + version + " SHA-256 " + Core.Hash(args[0]));
}

Console.WriteLine($"PASS {passed} update release validation assertions");

static Version ReadPeVersion(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
    int rootRva = pe.PEHeaders.PEHeader!.ResourceTableDirectory.RelativeVirtualAddress;
    var resource = pe.GetSectionData(rootRva).GetContent().ToArray();
    uint U32(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(resource.AsSpan(offset, 4));
    ushort U16(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(resource.AsSpan(offset, 2));
    int Child(int directory, uint? id)
    {
        int count = U16(directory + 12) + U16(directory + 14);
        for (int i = 0; i < count; i++)
        {
            int entry = directory + 16 + i * 8;
            if (id == null || U32(entry) == id) return checked((int)(U32(entry + 4) & 0x7fffffff));
        }
        throw new IOException("Missing RT_VERSION resource");
    }
    int leaf = Child(Child(Child(0, 16), null), null);
    var blob = pe.GetSectionData(checked((int)U32(leaf))).GetContent(0, checked((int)U32(leaf + 4))).ToArray();
    uint Value(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
    if (Value(40) != 0xfeef04bd) throw new IOException("Invalid VS_FIXEDFILEINFO");
    uint majorMinor = Value(48), buildRevision = Value(52);
    return new Version((int)(majorMinor >> 16), (int)(majorMinor & 65535), (int)(buildRevision >> 16), (int)(buildRevision & 65535));
}

sealed class UpdateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
}
