using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using AmdNrAssistant;
using Mu.Compatibility;

int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    passed++;
    Console.WriteLine("PASS " + label);
}
async Task Throws(Func<Task> action, Func<Exception, bool> expected, string label)
{
    try { await action(); throw new Exception("Expected failure: " + label); }
    catch (Exception e) { Check(expected(e), label); }
}

const string dxdiag = """
    <DxDiag>
      <SystemInformation>
        <OperatingSystem>Windows 11 Pro 64-bit (10.0, Build 26100)</OperatingSystem>
        <DirectXVersion>DirectX 12</DirectXVersion>
        <MachineName>PRIVATE-MACHINE</MachineName>
        <MachineId>{PRIVATE-ID}</MachineId>
      </SystemInformation>
      <DisplayDevices>
        <DisplayDevice>
          <CardName>AMD Radeon RX 7900 XT</CardName>
          <Manufacturer>Advanced Micro Devices, Inc.</Manufacturer>
          <VendorID>0x1002</VendorID>
          <DedicatedMemory>20464 MB</DedicatedMemory>
          <DisplayMemory>53232 MB</DisplayMemory>
          <SharedMemory>32768 MB</SharedMemory>
          <DriverVersion>32.0.21013.1000</DriverVersion>
          <DriverName>C:\Windows\System32\DriverStore\private.dll</DriverName>
        </DisplayDevice>
        <DisplayDevice>
          <CardName>Intel(R) UHD Graphics 770</CardName>
          <Manufacturer>Intel Corporation</Manufacturer>
          <DedicatedMemory>128 MB</DedicatedMemory>
          <DisplayMemory>32896 MB</DisplayMemory>
          <DriverVersion>32.0.101.6732</DriverVersion>
        </DisplayDevice>
      </DisplayDevices>
    </DxDiag>
    """;
var snapshot = CompatibilityEnvironmentReader.ParseDxDiag(dxdiag);
Check(snapshot.Gpus.Count == 2, "fixture retains discrete and integrated GPUs for explicit selection");
Check(snapshot.Gpus[0].VramMb == 20464 && snapshot.Gpus[1].VramMb == 128, "dedicated VRAM stays above 4 GiB without counting shared memory");
Check(snapshot.Gpus[0].Vendor == "AMD" && snapshot.Gpus[1].Vendor == "Intel", "GPU vendors normalized from measured identity");
Check(snapshot.Gpus[0].DriverVersion == "32.0.21013.1000", "driver version retained independently of tool version");
Check(snapshot.SystemDirectX == "DirectX 12", "system DirectX parsed as system information only");
Check(snapshot.Gpus.All(gpu => gpu.Architecture == "unknown"), "unmeasured architecture is not guessed");
string snapshotJson = JsonSerializer.Serialize(snapshot);
Check(!snapshotJson.Contains("PRIVATE") && !snapshotJson.Contains("DriverStore"), "machine identity and driver paths excluded from output");

Check(CompatibilityEnvironmentReader.ParseMemoryMb("24 GB") == 24576, "GB memory normalized to MB");
Check(CompatibilityEnvironmentReader.ParseMemoryMb("20,464 MB") == 20464, "grouped integer memory parsed without truncation");
Check(CompatibilityEnvironmentReader.ParseMemoryMb("1.5 GB") == 1536, "fractional GB memory parsed invariantly");
Check(CompatibilityEnvironmentReader.ParseMemoryMb("21458059264 bytes") == 20464, "64-bit memory byte values do not overflow");
Check(CompatibilityEnvironmentReader.ParseMemoryMb("unknown") == null
    && CompatibilityEnvironmentReader.ParseMemoryMb("-1 MB") == null
    && CompatibilityEnvironmentReader.ParseMemoryMb("0 MB") == null, "unknown invalid and zero VRAM stay unknown");
var unknown = CompatibilityEnvironmentReader.ParseDxDiag("<DxDiag><DisplayDevices><DisplayDevice><CardName>Unidentified GPU</CardName><DisplayMemory>8192 MB</DisplayMemory></DisplayDevice></DisplayDevices></DxDiag>");
Check(unknown.Gpus.Single().VramMb == null && unknown.Gpus.Single().DriverVersion == "unknown", "total memory never substituted for missing dedicated memory");
Check(unknown.SystemDirectX == "unknown" && unknown.OsVersion == "unknown", "missing diagnostic fields remain unknown");
Check(CompatibilityEnvironmentReader.ParseDxDiag("<DxDiag/>").Gpus.Count == 0, "empty dxdiag contains no invented GPU");
var hybrid = CompatibilityEnvironmentReader.ParseDxDiag("<DxDiag><RenderDevices><RenderDevice><CardName>NVIDIA GeForce RTX 4090</CardName><VendorID>0x10DE</VendorID><DedicatedMemory>24564 MB</DedicatedMemory><DriverVersion>32.0.15.1234</DriverVersion></RenderDevice></RenderDevices></DxDiag>");
Check(hybrid.Gpus.Single().Vendor == "NVIDIA", "render-only GPU section is available for hybrid systems");
await Throws(() => Task.FromResult(CompatibilityEnvironmentReader.ParseDxDiag("<!DOCTYPE x [<!ENTITY private SYSTEM 'file:///private'>]><DxDiag>&private;</DxDiag>")),
    e => e is XmlException, "XML external entities and DTD are prohibited");

const string acf = """
    "AppState"
    {
      "appid" "1091500"
      "installdir" "Cyberpunk 2077"
      "buildid" "20123456"
      "UserConfig" { "buildid" "999" "installdir" "Other Game" }
      "LastOwner" "PRIVATE-ACCOUNT"
    }
    """;
Check(CompatibilityEnvironmentReader.ParseSteamBuild(acf, "Cyberpunk 2077") == "20123456", "Steam build belongs to matching game and ignores nested keys");
Check(CompatibilityEnvironmentReader.ParseSteamBuild(acf, "Other Game") == "unknown", "unrelated Steam manifest is not attributed to selected game");
Check(CompatibilityEnvironmentReader.ParseSteamBuild(acf.Replace("20123456", "C:\\private"), "Cyberpunk 2077") == "unknown", "invalid build identifiers are not reported");

var reader = new CompatibilityEnvironmentReader("2.0.0-preview.2");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    await Throws(() => reader.ReadGpusAsync(cancelled.Token), e => e is OperationCanceledException, "GPU read observes cancellation before platform work");
    await Throws(() => reader.ReadAsync("missing.exe", null, snapshot.Gpus[0], cancelled.Token), e => e is OperationCanceledException, "game environment read observes cancellation");
}
if (!OperatingSystem.IsWindows())
{
    var fallback = await reader.ReadGpusAsync(default);
    Check(fallback.Single().Name == "unknown" && reader.Warning != null, "non-Windows detection provides editable unknown fallback and warning");
}
var environment = await reader.ReadAsync(Path.Combine(Path.GetTempPath(), "private-user", "missing-game.exe"), null, snapshot.Gpus[0], default);
Check(environment.Gpu == snapshot.Gpus[0] && environment.ToolVersion == "2.0.0-preview.2", "selected GPU and supplied tool version are preserved");
Check(environment.GameVersion == "unknown" && environment.DlssVersion == "unknown" && environment.Dlss5Version == "unknown", "missing executables and DLLs never imply a version");
Check(environment.RenderApi == "unknown" && environment.Settings == "unknown" && environment.OtherMods == "unknown", "unmeasured game API settings and mods remain unknown");
Check(!JsonSerializer.Serialize(environment).Contains("private-user"), "local executable path is not part of submitted environment");
var defaultEnvironment = await new CompatibilityEnvironmentReader().ReadAsync("missing-game.exe", null, snapshot.Gpus[0], default);
string informationalVersion = typeof(CompatibilityEnvironmentReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
Check(defaultEnvironment.ToolVersion == informationalVersion && defaultEnvironment.ToolVersion.StartsWith("2.0.0-preview.2"),
    "default reader reports running assembly informational version with prerelease suffix and build metadata");
Check(defaultEnvironment.ToolVersion != typeof(CompatibilityEnvironmentReader).Assembly.GetName().Version!.ToString(3),
    "default reader does not collapse prerelease identity to assembly version");
string longestCombinedVersion = CompatibilityEnvironmentReader.FormatGameVersion(new string('1', 64), new string('2', 20));
Check(longestCombinedVersion.Length <= 100 && longestCombinedVersion.Contains(new string('1', 64)) && longestCombinedVersion.EndsWith(new string('2', 20) + ")"),
    "longest accepted file and Steam build versions fit server field limit without truncation");
Check(CompatibilityEnvironmentReader.FormatGameVersion(new string('1', 90), "222") == "Steam build 222",
    "overlong combined game version falls back to complete Steam build within 100 characters");

string filesFixture = Path.Combine(Path.GetTempPath(), "mu-compatibility-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    string steamapps = Path.Combine(filesFixture, "steamapps");
    string installed = Path.Combine(steamapps, "common", "Fixture Game");
    string binary = Path.Combine(installed, "bin", "x64");
    Directory.CreateDirectory(binary);
    string executable = Path.Combine(binary, "game.exe");
    File.Copy(typeof(CompatibilityEnvironmentReader).Assembly.Location, executable);
    File.Copy(executable, Path.Combine(binary, "nvngx_dlss.dll"));
    File.Copy(executable, Path.Combine(binary, "nvngx_dlssnr.dll"));
    File.WriteAllText(Path.Combine(steamapps, "appmanifest_1.acf"), "\"AppState\" { \"installdir\" \"Unrelated Game\" \"buildid\" \"111\" }");
    File.WriteAllText(Path.Combine(steamapps, "appmanifest_2.acf"), "\"AppState\" { \"installdir\" \"Fixture Game\" \"buildid\" \"222\" }");
    var files = await reader.ReadAsync(executable, installed, snapshot.Gpus[0], default);
    Check(files.GameVersion.StartsWith("2.0.0.3") && files.GameVersion.EndsWith("(Steam build 222)"), "file fixture combines real PE file version with matching Steam build");
    Check(files.DlssVersion == "2.0.0.3" && files.Dlss5Version == "2.0.0.3", "DLL versions read from file metadata without inferring successful loading");
    Check(files.RenderApi == "unknown", "presence of DLL files does not infer active render API");
}
finally { Directory.Delete(filesFixture, true); }

var account = new AccountSnapshot("user-1", "player@example.com", new("standard", null), [], false);
SessionResponse Session(string suffix) => new("access-" + suffix, "refresh-" + suffix, DateTimeOffset.UtcNow.AddMinutes(15), account);
HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
HttpResponseMessage Error(HttpStatusCode status) => new(status) { Content = JsonContent.Create(new { code = "test_error", message = "Fixture error" }) };
var game = new CompatibilityGame("game-1", "Game & Beta", "123");
var summary = new CompatibilitySummary(game, new(3, 2, 1), "partial", DateTimeOffset.UtcNow);
var detail = new CompatibilityDetail(game, summary.Counts, summary.Status, summary.LastTestedAt, [], []);
var submission = new CompatibilitySubmission(Guid.NewGuid().ToString("N"), game.Id, game.Name, game.SteamAppId, environment, "success", null, null);
var test = new CompatibilityTest("test-1", game.Id, "Player", environment, "success", null, null, DateTimeOffset.UtcNow);
var calls = new List<(string Path, string? Bearer, string Body)>();
using (var client = new AccountApiClient(new HttpClient(new Handler(async (request, token) =>
{
    string route = request.RequestUri!.PathAndQuery;
    calls.Add((route, request.Headers.Authorization?.Parameter, request.Content == null ? "" : await request.Content.ReadAsStringAsync(token)));
    if (route.EndsWith("auth/login")) return Ok(Session("initial"));
    if (route.Contains("/games?q=")) return Ok(new CompatibilitySearchResponse([summary]));
    if (route.Contains("/games/game-1")) return Ok(detail);
    return Ok(test);
})), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    var search = await client.SearchCompatibilityAsync("Game & Beta", "AMD RX 7900 XT", default);
    Check(search.Items.Single().Counts.Total == 6, "search API response deserializes complete result counts");
    Check(calls[^1].Path == "/api/v1/compatibility/games?q=Game%20%26%20Beta&gpu=AMD%20RX%207900%20XT", "search and GPU filters encoded as separate query parameters");
    var found = await client.GetCompatibilityAsync(game.Id, null, default);
    Check(found.Game == game && calls[^1].Path == "/api/v1/compatibility/games/game-1", "detail URL omits empty GPU filter");
    var saved = await client.SubmitCompatibilityAsync(submission, default);
    Check(saved.Id == "test-1" && calls[^1].Path == "/api/v1/compatibility/tests", "submit API uses expected route and response contract");
    Check(calls.Skip(1).All(call => call.Bearer == "access-initial"), "all compatibility business APIs carry authenticated access token");
    using var json = JsonDocument.Parse(calls[^1].Body);
    Check(json.RootElement.GetProperty("submissionId").GetString() == submission.SubmissionId
        && json.RootElement.GetProperty("environment").GetProperty("gpu").GetProperty("name").GetString() == environment.Gpu.Name,
        "submission contract includes caller-generated ID and nested environment");
}

int refreshes = 0, posts = 0;
var bodies = new List<string>();
using (var client = new AccountApiClient(new HttpClient(new Handler(async (request, token) =>
{
    string path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("login")) return Ok(Session("old"));
    if (path.EndsWith("refresh")) { refreshes++; return Ok(Session("new")); }
    posts++;
    bodies.Add(await request.Content!.ReadAsStringAsync(token));
    if (posts == 1) return Error(HttpStatusCode.Unauthorized);
    Check(request.Headers.Authorization?.Parameter == "access-new", "401 retry sends refreshed bearer token");
    return Ok(test);
})), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await client.SubmitCompatibilityAsync(submission, default);
    Check(posts == 2 && refreshes == 1 && bodies[0] == bodies[1], "401 refresh retry preserves exact body and idempotency ID");
}

posts = 0;
bodies.Clear();
using (var client = new AccountApiClient(new HttpClient(new Handler(async (request, token) =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Ok(Session("retry"));
    bodies.Add(await request.Content!.ReadAsStringAsync(token));
    if (++posts == 1) throw new HttpRequestException("fixture connection lost after request");
    return Ok(test);
})), new MemoryStore()))
{
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.SubmitCompatibilityAsync(submission, default), e => e is HttpRequestException, "uncertain transport failure is returned to caller");
    Check(client.HasSession && !client.IsOnline, "transport failure retains recoverable session without marking online");
    await client.SubmitCompatibilityAsync(submission, default);
    Check(bodies.Count == 2 && bodies[0] == bodies[1] && client.IsOnline, "caller retry reuses submission ID and recovers online state");
}

foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.TooManyRequests })
{
    using var client = new AccountApiClient(new HttpClient(new Handler((request, token) => Task.FromResult(
        request.RequestUri!.AbsolutePath.EndsWith("login") ? Ok(Session("validation")) : Error(status)))), new MemoryStore());
    await client.LoginAsync(account.Email, "password123", false, default);
    await Throws(() => client.SubmitCompatibilityAsync(submission, default), e => e is AccountApiException api && api.Status == status, "business HTTP " + (int)status + " error reaches UI");
    Check(client.IsOnline && client.HasSession, "business HTTP " + (int)status + " error retains online account");
}

int anonymousCalls = 0;
using (var client = new AccountApiClient(new HttpClient(new Handler((request, token) =>
{
    anonymousCalls++;
    return Task.FromResult(Ok(test));
})), new MemoryStore()))
{
    await Throws(() => client.SearchCompatibilityAsync("", null, default), e => e is AccountApiException { Code: "login_required" }, "compatibility search requires login");
    await Throws(() => client.SubmitCompatibilityAsync(submission, default), e => e is AccountApiException { Code: "login_required" }, "compatibility submission requires login");
    Check(anonymousCalls == 0, "unauthenticated compatibility operations send no requests");
}
Console.WriteLine($"Compatibility fixture and API tests passed: {passed}. Windows hardware execution is not covered by these fixtures.");

sealed class MemoryStore : IAccountTokenStore
{
    public string? Read() => null;
    public void Write(string refreshToken) { }
    public void Clear() { }
}

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
}
