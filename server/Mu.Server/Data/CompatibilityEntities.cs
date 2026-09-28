namespace Mu.Server.Data;

public sealed class CompatibilityGameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string? SteamAppId { get; set; }
    public string? Developer { get; set; }
    public string? Engine { get; set; }
    public long CreatedAt { get; set; }
}

public sealed class CompatibilityTestEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string SubmissionId { get; set; } = "";
    public string SubmissionHash { get; set; } = "";
    public string GameId { get; set; } = "";
    public string GpuName { get; set; } = "";
    public string GpuKey { get; set; } = "";
    public string EnvironmentJson { get; set; } = "";
    public string EnvironmentHash { get; set; } = "";
    public string Result { get; set; } = "";
    public string? FailureReason { get; set; }
    public string? Notes { get; set; }
    public long CreatedAt { get; set; }
    public long TestDay { get; set; }
}
