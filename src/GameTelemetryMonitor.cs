using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mu.Telemetry;

namespace AmdNrAssistant;

// Runs only while MU is open and monitoring is enabled. Never injects into a game process.
public sealed class GameTelemetryMonitor(AccountApiClient api) : IDisposable
{
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim uploadGate = new(1, 1);
    readonly Dictionary<int, Task> sessions = new();
    readonly object gate = new();
    long dataGeneration;
    IReadOnlyList<GameCandidate> games = [];
    Task? worker;
    volatile bool enabled;
    volatile string? presentMonPath;
    public bool Enabled { get => enabled; set => enabled = value; }
    public string? PresentMonPath { get => presentMonPath; set => presentMonPath = value; }
    static string QueueDirectory(string userId) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "telemetry-queue",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..24]);

    public void SetGames(IReadOnlyList<GameCandidate> value) { lock (gate) games = value.ToArray(); }
    public void Start() => worker ??= Task.Run(() => RunAsync(lifetime.Token));

    async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Enabled)
                {
                    await UploadPendingAsync(token);
                    GameCandidate[] snapshot;
                    lock (gate) snapshot = games.ToArray();
                    var byPath = snapshot.Where(x => !string.IsNullOrWhiteSpace(x.ExePath) && IsConfigured(x.ExePath))
                        .GroupBy(x => Path.GetFullPath(x.ExePath), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
                    var names = byPath.Keys.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var process in Process.GetProcesses())
                    {
                        using (process)
                        {
                            if (sessions.ContainsKey(process.Id)) continue;
                            if (!names.Contains(process.ProcessName)) continue;
                            string? path;
                            try { path = process.MainModule?.FileName; }
                            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { continue; }
                            if (path == null || !byPath.TryGetValue(path, out var game) || api.Account is not { } owner) continue;
                            var pid = process.Id;
                            sessions[pid] = CaptureAsync(game, pid, owner.Id, token);
                        }
                    }
                    foreach (var pid in sessions.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToArray())
                    {
                        _ = sessions[pid].Exception;
                        sessions.Remove(pid);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
            { /* A failed poll cannot stop the client or an active game. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); } catch (OperationCanceledException) { break; }
        }
    }

    async Task CaptureAsync(GameCandidate game, int pid, string ownerId, CancellationToken token)
    {
        var generation = Interlocked.Read(ref dataGeneration);
        var id = Guid.NewGuid().ToString("D");
        var started = DateTimeOffset.UtcNow;
        var parts = new List<FrameSummary>();
        var status = "presentmon_unavailable";
        string? runtime = null;
        string? mon = PresentMonPath;
        if (mon != null && !File.Exists(mon)) mon = null;
        if (mon != null)
        {
            while (Enabled && generation == Interlocked.Read(ref dataGeneration) && !token.IsCancellationRequested && IsRunning(pid))
            {
                var csv = Path.Combine(Path.GetTempPath(), "mu-presentmon-" + Guid.NewGuid().ToString("N") + ".csv");
                try
                {
                    using var capture = Process.Start(new ProcessStartInfo(mon)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        ArgumentList = { "--process_id", pid.ToString(CultureInfo.InvariantCulture), "--timed", "30", "--terminate_after_timed", "--v2_metrics", "--no_console_stats", "--session_name", "MU-" + id[..8], "--output_file", csv }
                    });
                    if (capture == null) { status = "presentmon_start_failed"; break; }
                    while (!capture.HasExited && Enabled && generation == Interlocked.Read(ref dataGeneration) && IsRunning(pid) && !token.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    if (!capture.HasExited) { capture.Kill(entireProcessTree: true); await capture.WaitForExitAsync(CancellationToken.None); }
                    if (File.Exists(csv))
                    {
                        var sample = PresentMonCsv.Summarize(csv);
                        if (sample.FrameCount > 0) { parts.Add(sample); runtime ??= sample.Runtime; status = "captured"; }
                        else if (parts.Count == 0) status = "presentmon_no_frames";
                    }
                    else if (parts.Count == 0) status = "presentmon_no_frames";
                    if (capture.ExitCode != 0 && parts.Count == 0) { status = "presentmon_failed"; break; }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
                { status = "presentmon_failed"; break; }
                finally { try { File.Delete(csv); } catch (IOException) { } }
            }
        }
        else while (Enabled && generation == Interlocked.Read(ref dataGeneration) && IsRunning(pid) && !token.IsCancellationRequested)
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); } catch (OperationCanceledException) { break; }

        if (!Enabled || generation != Interlocked.Read(ref dataGeneration) || token.IsCancellationRequested) return;
        GameRecord? record = null;
        try { record = GameManagement.Read(game.ExePath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        var frames = parts.Sum(x => x.FrameCount);
        var fpsSource = parts.Select(x => x.FpsSource).Distinct().Count() == 1 ? parts[0].FpsSource : parts.Count > 0 ? "mixed" : null;
        double? avg = frames >= 120 && fpsSource != "mixed" ? parts.Sum(x => x.DurationMs) is var ms && ms > 0 ? frames * 1000d / ms : null : null;
        var slow = parts.SelectMany(x => x.FrameTimesMs).OrderByDescending(x => x).Take(Math.Max(1, (int)Math.Ceiling(frames * .01))).ToArray();
        double? low = frames >= 120 && fpsSource != "mixed" && slow.Length > 0 ? 1000d / slow.Average() : null;
        if (frames is > 0 and < 120) status = "insufficient_frames";
        if (fpsSource == "mixed") status = "mixed_metric";
        var gpuBusy = WeightedAverage(parts.Select(x => (x.AverageGpuBusyMs, x.FrameCount)));
        var cpuBusy = WeightedAverage(parts.Select(x => (x.AverageCpuBusyMs, x.FrameCount)));
        string? gameVersion = null, gpuName = null, driver = null;
        long? vram = null;
        try { if (File.Exists(game.ExePath)) gameVersion = FileVersionInfo.GetVersionInfo(game.ExePath).FileVersion; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        try
        {
            var gpus = await new CompatibilityEnvironmentReader().ReadGpusAsync(token);
            var gpu = gpus.Count == 1 ? gpus[0] : null;
            if (gpu != null && gpu.Name != CompatibilityEnvironmentReader.Unknown)
            { gpuName = gpu.Name; vram = gpu.VramMb; driver = gpu.DriverVersion == CompatibilityEnvironmentReader.Unknown ? null : gpu.DriverVersion; }
        }
        catch (OperationCanceledException) { return; }
        var upload = new GameTelemetryUpload(id, game.Title, game.SteamAppId,
            gameVersion,
            gpuName, vram, driver, ReadCpuName(), ReadMemoryMb(), Environment.OSVersion.VersionString,
            record?.Mode switch { 0 => "amd", 2 => "optiscaler", 3 => "live_panel", null => null, _ => "other" }, record?.Version,
            started, DateTimeOffset.UtcNow, runtime, frames, avg, low, gpuBusy, cpuBusy, fpsSource, status);
        await uploadGate.WaitAsync(CancellationToken.None);
        try
        {
            if (Enabled && generation == Interlocked.Read(ref dataGeneration)) SavePending(ownerId, upload);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { uploadGate.Release(); }
    }

    static double? WeightedAverage(IEnumerable<(double? Value, int Count)> samples)
    {
        var known = samples.Where(x => x.Value != null).ToArray();
        var count = known.Sum(x => x.Count);
        return count > 0 ? known.Sum(x => x.Value!.Value * x.Count) / count : null;
    }
    static bool IsConfigured(string exe)
    {
        try { return GameManagement.Read(exe) is { Phase: "installed" }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }
    static bool IsRunning(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
    static string? ReadCpuName()
    {
        try { return Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string; }
        catch (System.Security.SecurityException) { return null; }
    }
    static long? ReadMemoryMb()
    {
        var info = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref info) ? (long)(info.TotalPhysical / 1048576) : null;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    static void SavePending(string ownerId, GameTelemetryUpload item)
    {
        var directory = QueueDirectory(ownerId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, item.SessionId + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(item));
        File.Move(temp, path, true);
    }
    async Task UploadPendingAsync(CancellationToken token)
    {
        if (!api.HasSession || api.Account is not { } owner) return;
        await uploadGate.WaitAsync(token);
        try { await UploadPendingCoreAsync(owner.Id, token); }
        finally { uploadGate.Release(); }
    }
    async Task UploadPendingCoreAsync(string ownerId, CancellationToken token)
    {
        var directory = QueueDirectory(ownerId);
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Take(20))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(path).Length > 16 * 1024) { File.Move(path, path + ".rejected", true); continue; }
                var item = JsonSerializer.Deserialize<GameTelemetryUpload>(await File.ReadAllTextAsync(path, token));
                if (item == null) { File.Move(path, path + ".rejected", true); continue; }
                await api.SubmitTelemetryAsync(item, token);
                File.Delete(path);
            }
            catch (AccountApiException e) when (e.Status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
            { File.Move(path, path + ".rejected", true); }
            catch (JsonException) { File.Move(path, path + ".rejected", true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or AccountApiException or HttpRequestException or OperationCanceledException)
            { break; }
        }
    }
    public async Task DeleteMyDataAsync(CancellationToken token)
    {
        Enabled = false;
        Interlocked.Increment(ref dataGeneration);
        try
        {
            if (api.Account is not { } owner) throw new InvalidOperationException("请先登录账户。");
            await uploadGate.WaitAsync(token);
            try
            {
                var directory = QueueDirectory(owner.Id);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                await api.DeleteTelemetryAsync(token);
            }
            finally { uploadGate.Release(); }
        }
        finally { Enabled = true; }
    }
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
