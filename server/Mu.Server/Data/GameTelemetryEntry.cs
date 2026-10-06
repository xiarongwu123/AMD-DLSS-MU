namespace Mu.Server.Data;

public sealed class GameTelemetryEntry
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string GameName { get; set; } = "";
    public string GpuName { get; set; } = "";
    public string MuMode { get; set; } = "";
    public long StartedAt { get; set; }
    public long EndedAt { get; set; }
    public int FrameCount { get; set; }
    public double? AverageFps { get; set; }
    public double? OnePercentLowFps { get; set; }
    public string PayloadJson { get; set; } = "";
    public string PayloadHash { get; set; } = "";
}
