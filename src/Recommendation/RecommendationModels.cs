using System.Text.Json.Serialization;
namespace Mu.Recommendation;

public sealed record Observation(string Value = "unknown", string Confidence = "UNKNOWN", string Source = "未取得可靠证据")
{
    [JsonIgnore] public bool Known => Confidence != "UNKNOWN" && Value != "unknown";
    [JsonIgnore] public bool Confirmed => Confidence == "CONFIRMED" && Known;
}
public sealed record EnvironmentSnapshot
{
    [JsonIgnore] public string Executable { get; init; } = "";
    public string GameId { get; init; } = "";
    public string GameName { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public Dictionary<string, Observation> Fields { get; init; } = new();
    public Dictionary<string, string> Components { get; init; } = new();
    public Dictionary<string, bool> Resources { get; init; } = new();
    public string[] Conflicts { get; init; } = [];
    public string[] AntiCheat { get; init; } = [];
    public bool Running { get; init; }
    public bool BackupReady { get; init; }
    public bool HasConfiguration { get; init; }
    public bool IntegrityFailed { get; init; }
    public Observation Field(string key) => Fields.GetValueOrDefault(key) ?? new();
}
public sealed record SchemeCapability(string Id, string Name, string Version, bool Premium, bool External, string[] Apis, int[] Bitness);
public sealed record ConstraintIssue(string Code, string Message, string State);
public sealed record ResolvedConfiguration(string Scheme, string Backend, string ComponentVersion, string Proxy, int Bitness,
    string GraphicsApi, int Passes, Dictionary<string, Dictionary<string,string>> Ini, SmartParameters Parameters, string[] Dependencies);
public sealed record RecommendationCandidate(string Id, string Scheme, string Name, string State, double Score, string Level,
    bool Automatic, bool Premium, string Evidence, string[] Reasons, ConstraintIssue[] Issues, ResolvedConfiguration Configuration,
    string? RuleId, string? RuleVersion, string Fingerprint, bool External);
public sealed record RecommendationResult(string PlanId, string GameId, string GameName, EnvironmentSnapshot Environment,
    string Goal, string RuleRevision, string RuleSource, DateTimeOffset ExpiresUtc,
    RecommendationCandidate? Recommended, RecommendationCandidate? FreeAlternative, RecommendationCandidate[] Candidates,
    string[] Notices);
public sealed record VerificationResult(string State, bool Installed, bool Loaded, bool Active, bool ExperienceConfirmed,
    string[] FailureCodes, string[] Evidence, DateTimeOffset ObservedAt);
public sealed record FallbackAdvice(string Code, string Stage, string Message, string[] AlternativeIds, bool RestoreRequired, string[] Actions);
public sealed record RecommendationObservation(string DeviceId, string GameId, string GameBuild, string GpuModel, string Architecture,
    string DriverVersion, string Api, int Bitness, string Scheme, string Backend, string ComponentVersion, string PresetId,
    string RuleRevision, bool Installed, bool Loaded, bool Active, bool ExperienceConfirmed, bool? Crashed, string[] FailureCodes,
    string Feedback, DateTimeOffset VerifiedAt, string GameVersion = "unknown", string GpuDeviceId = "unknown");
