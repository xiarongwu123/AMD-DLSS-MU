using System.Globalization;

namespace AmdNrAssistant;

public sealed record FrameSummary(int FrameCount, double DurationMs, double[] FrameTimesMs, string? Runtime, double? AverageGpuBusyMs, double? AverageCpuBusyMs, string? FpsSource);

public static class PresentMonCsv
{
    public static FrameSummary Summarize(string path)
    {
        using var reader = new StreamReader(path);
        var header = reader.ReadLine()?.Split(',') ?? [];
        int Find(string name) => Array.FindIndex(header, x => x.Trim('"').Equals(name, StringComparison.OrdinalIgnoreCase));
        int interval = Find("MsBetweenDisplayChange");
        if (interval < 0) interval = Find("MsBetweenPresents");
        int gpu = Find("MsGPUBusy"), cpu = Find("MsCPUBusy"), api = Find("PresentRuntime");
        if (interval < 0) return new(0, 0, [], null, null, null, null);
        var fpsSource = Find("MsBetweenDisplayChange") >= 0 ? "display_change" : "present_interval";
        var times = new List<double>(); double gpuTotal = 0, cpuTotal = 0; int gpuCount = 0, cpuCount = 0; string? runtime = null;
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (interval >= fields.Length || !double.TryParse(fields[interval], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms is <= 0 or > 1000) continue;
            times.Add(ms);
            if (gpu >= 0 && gpu < fields.Length && double.TryParse(fields[gpu], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g >= 0) { gpuTotal += g; gpuCount++; }
            if (cpu >= 0 && cpu < fields.Length && double.TryParse(fields[cpu], NumberStyles.Float, CultureInfo.InvariantCulture, out var c) && c >= 0) { cpuTotal += c; cpuCount++; }
            if (runtime == null && api >= 0 && api < fields.Length) runtime = fields[api].Trim('"');
        }
        return new(times.Count, times.Sum(), times.ToArray(), runtime, gpuCount > 0 ? gpuTotal / gpuCount : null, cpuCount > 0 ? cpuTotal / cpuCount : null, fpsSource);
    }
}
