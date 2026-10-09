using AmdNrAssistant;
using System.Text.Json;
var current = new Version(1,2,0,0);
string Fixture(string tag = "v1.3.0", string? version = null, string? url = null,
    string? digest = null, string channel = "stable", string platform = "Windows x64",
    string file = AutoUpdate.AssetName, string delivery = "server", long size = 2048) => JsonSerializer.Serialize(new {
        version = version ?? tag[1..], tag, channel, platform, file, size,
        sha256 = digest ?? new string('a',64), downloadUrl = url ?? AutoUpdate.DownloadUrl, delivery
    });
int passed = 0;
void Assert(bool b) { if (!b) throw new Exception("assertion failed"); passed++; }
void Reject(string json) { try { AutoUpdate.Parse(json,current); } catch (IOException) { passed++; return; } throw new Exception("bad release accepted"); }
var websiteRelease = AutoUpdate.Parse(Fixture(), current);
Assert(websiteRelease?.Tag == "v1.3.0" && websiteRelease.Url == AutoUpdate.DownloadUrl &&
    websiteRelease.ApiUrl == null && websiteRelease.Sha256 == new string('a',64));
Assert(DownloadSources.Build(websiteRelease!.Url, websiteRelease.ApiUrl, websiteRelease.Sha256)
    .SequenceEqual(new[] { AutoUpdate.DownloadUrl }));
Assert(AutoUpdate.ManifestUrl == AutoUpdate.Website + "/release.json");
Assert(AutoUpdate.Parse(Fixture("v1.2.0"),current) == null);
Assert(AutoUpdate.Parse(Fixture("v1.1.0"),current) == null);
Assert(AutoUpdate.Parse(Fixture("v1.4.0"), new Version(2, 0, 0, 0)) == null);
Assert(AutoUpdate.Parse(Fixture("v2.0.1"), new Version(2, 0, 0, 0))?.Tag == "v2.0.1");
Assert(AutoUpdate.Parse(Fixture("v2.0.2"), new Version(2, 0, 1, 0))?.Tag == "v2.0.2");
Reject(Fixture(url:"https://github.com/xiarongwu123/AMD-DLSS-MU/releases/download/v1.3.0/AMD-DLSS-MU.exe"));
Reject(Fixture(url:"https://example.org/AMD-DLSS-MU.exe"));
Reject(Fixture(digest:"")); Reject(Fixture(digest:"sha256:bad"));
Reject(Fixture(channel:"beta")); Reject(Fixture(platform:"Windows ARM64"));
Reject(Fixture(file:"other.exe")); Reject(Fixture(delivery:"redirect"));
Reject(Fixture(version:"1.3.1"));
Reject(Fixture(size:0)); Reject(Fixture(size:2L*1024*1024*1024));
Reject(Fixture(tag:"v1.3.0-evil"));
if (args.Length is 1 or 2)
{
    Core.CheckPe(args[0], false);
    // FileVersionInfo on macOS returns empty for native bundled Windows hosts.
    // Read the actual RT_VERSION resource instead of treating that as version zero.
    var version = ReadPeVersion(args[0]);
    var expectedVersion = args.Length == 2 ? Version.Parse(args[1]) : new Version(2, 1, 0, 0);
    Assert(version == expectedVersion);
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
