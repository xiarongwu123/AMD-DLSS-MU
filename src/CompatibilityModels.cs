namespace Mu.Compatibility;

public sealed record CompatibilityGame(string Id, string Name, string? SteamAppId, string? Developer = null, string? Engine = null,
    CompatibilityCatalogInfo? Catalog = null);
public sealed record CompatibilityCatalogInfo(string Provider, string Url, DateTimeOffset RetrievedAt,
    string? LocalizedName, string? ReleaseDate, CompatibilityTechnologyReference[] References);
public sealed record CompatibilityTechnologyReference(string Provider, string Url, DateTimeOffset RetrievedAt,
    string MatchedTitle, string[] Features);
public sealed record CompatibilityGpu(string Name, string Vendor, long? VramMb, string? Architecture, string DriverVersion);
public sealed record CompatibilityEnvironment(
    CompatibilityGpu Gpu, string OsVersion, string SystemDirectX, string GameVersion,
    string RenderApi, string DlssVersion, string Dlss5Version, string ToolVersion,
    string Settings, string OtherMods);
public sealed record CompatibilityCounts(int Success, int Partial, int Failure)
{
    public int Total => Success + Partial + Failure;
}
public sealed record CompatibilityEvidence(bool HasRequirements, string? ModStatus);
public sealed record CompatibilityRequirementsTier(string? Text, string? Os, string? Processor, string? Memory,
    string? Graphics, string? DirectX, string? Storage, string? AdditionalNotes, long? MemoryMb, long? StorageMb);
public sealed record CompatibilityRequirements(string Provider, string Status, string? Reason, string SourceUrl,
    DateTimeOffset SourceRetrievedAt, string? SourceSha256, CompatibilityRequirementsTier? Minimum,
    CompatibilityRequirementsTier? Recommended);
public sealed record CompatibilityModEnvironment(string? OptiscalerVersion, string? Gpu, string? Os);
public sealed record CompatibilityAdaptation(string Id, string Name, string SourceProvider, string SourceUrl,
    DateTimeOffset SourceRetrievedAt, string SourceCommit, string Status, string[] UpscalerInputs,
    string RequiredMod, string[] Notes, CompatibilityModEnvironment? TestEnvironment, bool MuVerified);
public sealed record CompatibilitySummary(CompatibilityGame Game, CompatibilityCounts Counts, string Status,
    DateTimeOffset? LastTestedAt, CompatibilityEvidence? Evidence = null);
public sealed record CompatibilityCoverage(int Requirements, int ModCompatibility);
public sealed record CompatibilitySearchResponse(CompatibilitySummary[] Items, int Total = 0, int Page = 1,
    int PageSize = 50, int CatalogTotal = 0, CompatibilityCoverage? Coverage = null);
public sealed record CompatibilityGpuSummary(string GpuName, CompatibilityCounts Counts, string Status, DateTimeOffset? LastTestedAt);
public sealed record CompatibilityTest(string Id, string GameId, string Tester, CompatibilityEnvironment Environment,
    string Result, string? FailureReason, string? Notes, DateTimeOffset CreatedAt);
public sealed record CompatibilityDetail(CompatibilityGame Game, CompatibilityCounts Counts, string Status,
    DateTimeOffset? LastTestedAt, CompatibilityGpuSummary[] Gpus, CompatibilityTest[] Tests,
    CompatibilityRequirements? Requirements = null, CompatibilityAdaptation[]? ModCompatibility = null);
public sealed record CompatibilitySubmission(string SubmissionId, string? GameId, string GameName, string? SteamAppId,
    CompatibilityEnvironment Environment, string Result, string? FailureReason, string? Notes);
