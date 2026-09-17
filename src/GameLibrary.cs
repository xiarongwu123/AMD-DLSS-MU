using System.Text.Json;
using Microsoft.Win32;

namespace AmdNrAssistant;

public record LibraryEntry(string Title, string Directory, string? Exe = null);

public static class GameLibrary
{
    internal static string? StorageOverride { get; set; }
    static string Store => StorageOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "manual-games.json");
    static readonly object Gate = new();

    public static List<string> ReadManual()
    {
        lock (Gate)
        {
            if (!File.Exists(Store)) return new();
            if (new FileInfo(Store).Length > 1024 * 1024) throw new IOException("手动游戏列表过大，请备份后检查 manual-games.json。");
            try
            {
                return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Store)) ?? new())
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
            { throw new IOException("手动游戏列表损坏，请备份 manual-games.json 后重试。", e); }
        }
    }

    public static void AddManual(string exe)
    {
        lock (Gate)
        {
            var path = Path.GetFullPath(exe);
            var paths = ReadManual();
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Store)!);
            var temp = Store + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(paths));
            File.Move(temp, Store, true);
        }
    }

    public static LibraryEntry? ReadEpicManifest(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 1024 * 1024) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var root = json.RootElement;
            string? Get(string key) => root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var install = Get("InstallLocation");
            if (string.IsNullOrWhiteSpace(install) || !Path.IsPathFullyQualified(install)) return null;
            install = Path.GetFullPath(install);
            if (!System.IO.Directory.Exists(install)) return null;
            var title = Get("DisplayName");
            var launch = Get("LaunchExecutable");
            string? exe = null;
            if (!string.IsNullOrWhiteSpace(launch))
            {
                var resolved = Path.GetFullPath(Path.Combine(install, launch.Replace('\\', Path.DirectorySeparatorChar)));
                if (resolved.StartsWith(Path.TrimEndingDirectorySeparator(install) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetExtension(resolved), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
                    exe = resolved;
            }
            return new(string.IsNullOrWhiteSpace(title) ? Path.GetFileName(install) : title, install, exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return null; }
    }

    public static List<LibraryEntry> DiscoverRegistered()
    {
        var result = new List<LibraryEntry>();
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try
        {
            if (System.IO.Directory.Exists(manifests))
                foreach (var file in System.IO.Directory.EnumerateFiles(manifests, "*.item"))
                    if (ReadEpicManifest(file) is { } entry) result.Add(entry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!OperatingSystem.IsWindows()) return result;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var source in new[] { (Key: @"SOFTWARE\GOG.com\Games", Path: "path", Name: "gameName"), (Key: @"SOFTWARE\Ubisoft\Launcher\Installs", Path: "InstallDir", Name: "DisplayName") })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var games = registry.OpenSubKey(source.Key);
                if (games == null) continue;
                foreach (var id in games.GetSubKeyNames())
                {
                    try
                    {
                        using var game = games.OpenSubKey(id);
                        var path = game?.GetValue(source.Path) as string;
                        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path)) continue;
                        path = Path.GetFullPath(path);
                        result.Add(new(game?.GetValue(source.Name) as string ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), path));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return result;
    }
}
