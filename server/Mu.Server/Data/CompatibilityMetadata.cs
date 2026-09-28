using System.Globalization;
using System.Text.Json;
using Mu.Compatibility;

namespace Mu.Server.Data;

public static class CompatibilityMetadata
{
    private static readonly Lazy<Dictionary<string, CompatibilityRequirements>> RequirementSnapshot = new(() =>
        ReadResource("steam-requirements.json") is { } json ? ParseRequirements(json) : new(StringComparer.Ordinal));
    private static readonly Lazy<Dictionary<string, CompatibilityAdaptation[]>> AdaptationSnapshot = new(() =>
        ReadResource("optiscaler-compatibility.json") is { } json ? ParseAdaptations(json) : new(StringComparer.Ordinal));

    public static CompatibilityRequirements? Requirements(string? steamAppId) => steamAppId != null
        ? RequirementSnapshot.Value.GetValueOrDefault(steamAppId) : null;

    public static CompatibilityAdaptation[] Adaptations(string? steamAppId) => steamAppId != null
        ? AdaptationSnapshot.Value.GetValueOrDefault(steamAppId, []) : [];

    public static CompatibilityEvidence Evidence(string? steamAppId)
    {
        var states = Adaptations(steamAppId).Select(x => x.Status).Distinct(StringComparer.Ordinal).ToArray();
        return new(Requirements(steamAppId)?.Status == "available", states.Length switch
        {
            0 => null, 1 => states[0], _ => "mixed"
        });
    }

    public static CompatibilityCoverage Coverage(IEnumerable<string?> steamAppIds)
    {
        var ids = steamAppIds.Where(x => x != null).Distinct(StringComparer.Ordinal).ToArray();
        return new(ids.Count(x => Requirements(x)?.Status == "available"), ids.Count(x => Adaptations(x).Length > 0));
    }

    internal static Dictionary<string, CompatibilityRequirements> ParseRequirements(string json)
    {
        var source = Deserialize<RequirementCatalog>(json);
        Require(source.SchemaVersion == 1 && source.Games is { Length: <= 50000 }, "requirements catalog");
        var output = new Dictionary<string, CompatibilityRequirements>(StringComparer.Ordinal);
        foreach (var row in source.Games!)
        {
            Require(row != null && SteamIdentity(row.SteamAppId) && CompatibilityCatalog.FindInfo(row.SteamAppId) != null,
                "requirements game identity");
            Require(Text(row!.Name, 200) && row.Status is "available" or "not_provided" or "unavailable"
                && OptionalText(row.Reason, 1000) && ValidSteamSource(row.SourceUrl, row.SteamAppId)
                && ValidDate(row.SourceRetrievedAt) && (row.SourceSha256 == null || Hex(row.SourceSha256, 64)), "requirements source");
            ValidateTier(row.Minimum);
            ValidateTier(row.Recommended);
            var hasContent = !string.IsNullOrWhiteSpace(row.Minimum?.Text) || !string.IsNullOrWhiteSpace(row.Recommended?.Text);
            Require(row.Status == "available" ? hasContent && row.SourceSha256 != null
                : row.Minimum == null && row.Recommended == null && row.Reason != null
                    && (row.Status != "not_provided" || row.SourceSha256 != null),
                "requirements availability");
            Require(output.TryAdd(row.SteamAppId, new("Steam Store", row.Status, row.Reason, row.SourceUrl,
                row.SourceRetrievedAt, row.SourceSha256, row.Minimum, row.Recommended)), "duplicate requirements identity");
        }
        return output;
    }

    internal static Dictionary<string, CompatibilityAdaptation[]> ParseAdaptations(string json)
    {
        var source = Deserialize<AdaptationCatalog>(json);
        Require(source.SchemaVersion == 1 && source.Entries is { Length: <= 50000 }, "adaptation catalog");
        Require(source.Source != null && Text(source.Source.Provider, 100) && Hex(source.Source.Commit, 40)
            && ValidWikiSource(source.Source.Url, source.Source.Commit) && ValidDate(source.Source.RetrievedAt)
            && Hex(source.Source.Sha256, 64), "adaptation catalog source");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var output = new Dictionary<string, List<CompatibilityAdaptation>>(StringComparer.Ordinal);
        foreach (var row in source.Entries!)
        {
            Require(row != null && Text(row.Id, 100) && row.Id.StartsWith("optiscaler-", StringComparison.Ordinal)
                && identities.Add(row.Id) && Text(row.Name, 300) && Text(row.SourceProvider, 100)
                && Hex(row.SourceCommit, 40) && row.SourceCommit == source.Source!.Commit
                && ValidWikiSource(row.SourceUrl, row.SourceCommit) && ValidDate(row.SourceRetrievedAt)
                && row.Status is "working" or "not_working" or "platform_limited"
                && row.RequiredMod is "none" or "third_party_upscaler" or "luma_ue" && !row.MuVerified,
                "adaptation entry");
            Require(row!.UpscalerInputs is { Length: <= 20 } && row.UpscalerInputs.All(x => Text(x, 100))
                && row.Notes is { Length: <= 64 } && row.Notes.All(x => Text(x, 2048, multiline: true)), "adaptation notes");
            if (row.TestEnvironment is { } env)
                Require(OptionalText(env.OptiscalerVersion, 1000) && OptionalText(env.Gpu, 1000)
                    && OptionalText(env.Os, 1000), "adaptation environment");
            if (row.SteamAppId == null) continue;
            Require(SteamIdentity(row.SteamAppId) && CompatibilityCatalog.FindInfo(row.SteamAppId) != null,
                "adaptation Steam identity");
            if (!output.TryGetValue(row.SteamAppId, out var entries)) output.Add(row.SteamAppId, entries = []);
            entries.Add(new(row.Id, row.Name, row.SourceProvider, row.SourceUrl, row.SourceRetrievedAt,
                row.SourceCommit, row.Status, row.UpscalerInputs!, row.RequiredMod, row.Notes!, row.TestEnvironment, false));
        }
        return output.ToDictionary(x => x.Key, x => x.Value.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateTier(CompatibilityRequirementsTier? tier)
    {
        if (tier == null) return;
        Require(OptionalText(tier.Text, 32768, multiline: true)
            && new[] { tier.Os, tier.Processor, tier.Memory, tier.Graphics, tier.DirectX, tier.Storage, tier.AdditionalNotes }
                .All(x => OptionalText(x, 8192, multiline: true))
            && (tier.MemoryMb == null || tier.MemoryMb is > 0 and <= 1048576)
            && (tier.StorageMb == null || tier.StorageMb is > 0 and <= 1073741824)
            && (tier.MemoryMb == null || !string.IsNullOrWhiteSpace(tier.Memory))
            && (tier.StorageMb == null || !string.IsNullOrWhiteSpace(tier.Storage))
            && (new[] { tier.Os, tier.Processor, tier.Memory, tier.Graphics, tier.DirectX, tier.Storage, tier.AdditionalNotes }
                .All(x => x == null) || !string.IsNullOrWhiteSpace(tier.Text)), "requirements tier");
    }

    private static bool ValidSteamSource(string value, string appId)
    {
        if (!SafeUri(value, "store.steampowered.com", out var uri)) return false;
        if (uri.AbsolutePath == $"/app/{appId}/") return uri.Query.Length == 0;
        if (uri.AbsolutePath != "/api/appdetails") return false;
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).ToArray();
        return parameters.All(x => x.Length == 2 && x[0] is "appids" or "l" or "cc")
            && parameters.Count(x => x[0] == "appids" && x[1] == appId) == 1
            && parameters.Select(x => x[0]).Distinct(StringComparer.Ordinal).Count() == parameters.Length;
    }

    private static bool ValidWikiSource(string value, string commit)
    {
        if (!SafeUri(value, "github.com", out var uri) || uri.Query.Length != 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 5 && parts[0] == "optiscaler" && parts[1] == "OptiScaler" && parts[2] == "wiki"
            && parts[3].Length > 0 && parts[4] == commit;
    }

    private static bool SafeUri(string value, string host, out Uri uri)
    {
        uri = null!;
        return value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == "https"
            && uri.Host == host && uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Fragment.Length == 0;
    }

    private static bool SteamIdentity(string value) => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && id > 0 && id.ToString(CultureInfo.InvariantCulture) == value;
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidDate(DateTimeOffset value) => value >= new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
        && value <= DateTimeOffset.UtcNow.AddDays(1);
    private static bool OptionalText(string? value, int maximum, bool multiline = false) => value == null || Text(value, maximum, multiline);
    private static bool Text(string? value, int maximum, bool multiline = false) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(x => char.IsControl(x) && !(multiline && x is '\r' or '\n' or '\t'));
    private static void Require(bool condition, string context)
    {
        if (!condition) throw new InvalidDataException("Invalid reviewed compatibility metadata: " + context);
    }

    private static T Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing compatibility metadata content.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid compatibility metadata JSON.", ex); }
    }

    private static string? ReadResource(string name)
    {
        using var stream = typeof(CompatibilityMetadata).Assembly.GetManifestResourceStream("Mu.Server.Data.Catalog." + name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record RequirementCatalog(int SchemaVersion, RequirementEntry?[]? Games);
    private sealed record RequirementEntry(string SteamAppId, string Name, string Status, string? Reason,
        string SourceUrl, DateTimeOffset SourceRetrievedAt, string? SourceSha256,
        CompatibilityRequirementsTier? Minimum, CompatibilityRequirementsTier? Recommended);
    private sealed record AdaptationCatalog(int SchemaVersion, AdaptationSource? Source, AdaptationEntry?[]? Entries);
    private sealed record AdaptationSource(string Provider, string Url, string Commit, DateTimeOffset RetrievedAt, string Sha256);
    private sealed record AdaptationEntry(string Id, string? SteamAppId, string Name, string SourceProvider, string SourceUrl,
        DateTimeOffset SourceRetrievedAt, string SourceCommit, string Status, string[]? UpscalerInputs,
        string RequiredMod, string[]? Notes, CompatibilityModEnvironment? TestEnvironment, bool MuVerified);
}
