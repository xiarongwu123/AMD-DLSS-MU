using System.Text;

namespace AmdNrAssistant;

public static class GameSearch
{
    // Public title aliases only. Steam identity takes precedence over folder names
    // so the original game and its sequels do not inherit each other's exact names.
    static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["244210"] = ["Assetto Corsa", "神力科莎", "Assetto Corsa Ultimate Edition", "神力科莎终极版"],
        ["805550"] = ["Assetto Corsa Competizione", "神力科莎：竞速", "神力科莎：競速"],
        ["3058630"] = ["Assetto Corsa EVO", "神力科莎 EVO"],
        ["3917090"] = ["Assetto Corsa Rally", "神力科莎：拉力"]
    };

    static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)).ToLowerInvariant();

    public static string[] Terms(string title, string? steamAppId, string? installDirectory = null)
    {
        var folder = installDirectory?.Replace('\\', '/').TrimEnd('/').Split('/').LastOrDefault();
        var terms = new List<string> { title };
        if (!string.IsNullOrWhiteSpace(folder)) terms.Add(folder);
        if (!string.IsNullOrWhiteSpace(steamAppId))
        {
            terms.Add(steamAppId);
            if (Aliases.TryGetValue(steamAppId, out var aliases)) terms.AddRange(aliases);
        }
        else
        {
            // Manually added acs.exe entries have no Steam ID. Match complete
            // known titles/folders, never a loose "assetto" prefix.
            var names = terms.Select(Normalize).ToHashSet(StringComparer.Ordinal);
            foreach (var aliases in Aliases.Values)
                if (aliases.Any(alias => names.Contains(Normalize(alias)))) terms.AddRange(aliases);
        }
        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool Matches(string title, string? steamAppId, string? installDirectory, string query)
    {
        var search = Normalize(query);
        return search.Length == 0 || Terms(title, steamAppId, installDirectory).Any(term => Normalize(term).Contains(search, StringComparison.Ordinal));
    }
}
