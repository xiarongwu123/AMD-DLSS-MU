namespace Mu.Telemetry;

// A session is an observation, not a claim that the game or a graphics feature worked.
public sealed record GameTelemetryUpload(
    string SessionId, string GameName, string? SteamAppId, string? GameVersion,
    string? GpuName, long? VramMb, string? DriverVersion, string? CpuName,
    long? MemoryMb, string? WindowsVersion, string? MuMode, string? MuVersion,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, string? PresentRuntime,
    int FrameCount, double? AverageFps, double? OnePercentLowFps,
    double? AverageGpuBusyMs, double? AverageCpuBusyMs, string? FpsSource,
    string CaptureStatus);

public sealed record GameTelemetryReceipt(string SessionId, bool Stored);
