using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Compatibility;

namespace Mu.Server.Data;

public static class CompatibilityCatalog
{
    private static readonly Lazy<CatalogSnapshot> Snapshot = new(ReadSnapshot);
    private static readonly Dictionary<string, (string Id, string Aliases)> Legacy = new()
    {
        ["1091500"] = ("cyberpunk-2077", "赛博朋克 2077 赛博朋克2077"),
        ["271590"] = ("gta-v-legacy", "GTA V GTA5 GTA 5 侠盗猎车手 传统版"),
        ["3240220"] = ("gta-v-enhanced", "GTA V GTA5 GTA 5 侠盗猎车手 增强版"),
        ["2358720"] = ("black-myth-wukong", "黑神话 悟空 黑神话悟空")
    };

    public static CompatibilityCatalogInfo? FindInfo(string? steamAppId) =>
        steamAppId != null && Snapshot.Value.Info.TryGetValue(steamAppId, out var info) ? info : null;

    public static async Task SeedAsync(AppDbContext db)
    {
        var present = await db.CompatibilityGames.Where(x => x.SteamAppId != null).ToDictionaryAsync(x => x.SteamAppId!);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var entry in Snapshot.Value.Games)
        {
            Legacy.TryGetValue(entry.SteamAppId, out var legacy);
            var searchText = $"{entry.Name} {entry.NameZhCn} {legacy.Aliases} {entry.SteamAppId}".ToUpperInvariant();
            if (present.TryGetValue(entry.SteamAppId, out var game))
            {
                // Keep the original game ID and user reports when official metadata is refreshed.
                game.Name = entry.Name;
                game.NormalizedName = entry.Name.ToUpperInvariant();
                if (!game.SearchText.Contains(searchText, StringComparison.Ordinal))
                    game.SearchText = (searchText + " " + game.SearchText).Trim();
            }
            else
            {
                db.CompatibilityGames.Add(new()
                {
                    Id = legacy.Id ?? "steam-" + entry.SteamAppId,
                    Name = entry.Name, NormalizedName = entry.Name.ToUpperInvariant(), SteamAppId = entry.SteamAppId,
                    SearchText = searchText, CreatedAt = now
                });
            }
        }
        // Catalog records describe game identities; compatibility test rows are never generated here.
    }

    private static CatalogSnapshot ReadSnapshot()
    {
        var catalog = ReadResource<SteamCatalog>("steam-games.json");
        if (catalog.SchemaVersion != 1 || catalog.Games.Length < 1000 || catalog.Games.Length > 50000)
            throw new InvalidDataException("The reviewed Steam catalog must contain 1000-50000 games.");
        var references = ReadResource<ReferenceCatalog>("technology-references.json", optional: true)?.Entries ?? [];
        var grouped = references.GroupBy(x => x.SteamAppId).ToDictionary(x => x.Key, x => x.ToArray());
        var info = new Dictionary<string, CompatibilityCatalogInfo>(StringComparer.Ordinal);
        foreach (var entry in catalog.Games)
        {
            if (!uint.TryParse(entry.SteamAppId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0
                || id.ToString(CultureInfo.InvariantCulture) != entry.SteamAppId || !ValidText(entry.Name, 200)
                || (entry.NameZhCn != null && !ValidText(entry.NameZhCn, 200))
                || entry.StoreUrl != $"https://store.steampowered.com/app/{entry.SteamAppId}/"
                || !DateOnly.TryParseExact(entry.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate)
                || !DateTimeOffset.TryParse(entry.SourceRetrievedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var retrievedAt)
                || releaseDate > DateOnly.FromDateTime(retrievedAt.UtcDateTime))
                throw new InvalidDataException($"Invalid Steam catalog identity: {entry.SteamAppId}");
            var technical = grouped.GetValueOrDefault(entry.SteamAppId, []).Select(reference =>
            {
                if (!ValidText(reference.Provider, 64) || !ValidText(reference.MatchedTitle, 200)
                    || reference.Features.Length is < 1 or > 20 || reference.Features.Any(x => !ValidText(x, 100))
                    || !Uri.TryCreate(reference.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "www.nvidia.com"
                    || url.UserInfo.Length != 0 || !url.IsDefaultPort || url.AbsolutePath != "/en-us/geforce/news/nvidia-rtx-games-engines-apps/"
                    || url.Query.Length != 0 || url.Fragment.Length != 0
                    || !DateTimeOffset.TryParse(reference.RetrievedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                    throw new InvalidDataException($"Invalid technical reference: {entry.SteamAppId}");
                return new CompatibilityTechnologyReference(reference.Provider, reference.Url, at, reference.MatchedTitle, reference.Features);
            }).ToArray();
            if (!info.TryAdd(entry.SteamAppId, new("Steam Store", entry.StoreUrl, retrievedAt, entry.NameZhCn, entry.ReleaseDate, technical)))
                throw new InvalidDataException($"Duplicate Steam catalog identity: {entry.SteamAppId}");
        }
        if (grouped.Keys.Any(id => !info.ContainsKey(id))) throw new InvalidDataException("Technical reference has no reviewed Steam identity.");
        return new(catalog.Games, info);
    }

    private static bool ValidText(string value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(char.IsControl);

    private static T ReadResource<T>(string file, bool optional = false) where T : class
    {
        var assembly = typeof(CompatibilityCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("Mu.Server.Data.Catalog." + file);
        if (stream == null)
        {
            if (optional) return null!;
            throw new InvalidDataException("Missing reviewed catalog resource: " + file);
        }
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid catalog resource: " + file);
    }

    private sealed record SteamCatalog(int SchemaVersion, SteamGame[] Games);
    private sealed record SteamGame(string SteamAppId, string Name, string? NameZhCn, string ReleaseDate, string StoreUrl, string SourceRetrievedAt);
    private sealed record ReferenceCatalog(int SchemaVersion, ReferenceEntry[] Entries);
    private sealed record ReferenceEntry(string SteamAppId, string Provider, string Url, string RetrievedAt, string MatchedTitle, string[] Features);
    private sealed record CatalogSnapshot(SteamGame[] Games, Dictionary<string, CompatibilityCatalogInfo> Info);
}
