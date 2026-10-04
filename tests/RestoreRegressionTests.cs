using AmdNrAssistant;
using System.Text.Json;

static class RestoreRegressionTests
{
    public static void Run(string root, Action<bool, string> assert)
    {
        string Game(string name)
        {
            var dir = Path.Combine(root, "restore-regression", name);
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, name + ".exe"); File.WriteAllText(exe, "fixture"); return exe;
        }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (IOException) { assert(true, name); return; }
            catch (UnauthorizedAccessException) { assert(true, name); return; }
            throw new Exception("Expected rejection: " + name);
        }
        void AutoRestore(string exe) => GameManagement.Restore(exe, true, GameManagement.RestoreConflicts(exe));

        var game = Game("nested"); var dir = Path.GetDirectoryName(game)!;
        var nested = Path.Combine(dir, "D3D12_Optiscaler", "D3D12Core.dll");
        GameManagement.Begin(game, 2);
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!); File.WriteAllText(nested, "installed");
        GameManagement.Finish(game, true); File.WriteAllText(nested, "updated nested");
        AutoRestore(game);
        var archived = Directory.GetFiles(GameManagement.State(game), "D3D12Core.dll", SearchOption.AllDirectories).Single();
        assert(!File.Exists(nested) && File.ReadAllText(archived) == "updated nested", "nested conflict archived intact before removal");
        var manifest = Directory.GetFiles(GameManagement.State(game), "conflicts.json", SearchOption.AllDirectories).Single();
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))!;
        assert(entries.Values.Single() == Core.Hash(archived), "archive manifest records archived bytes");
        File.WriteAllText(nested, "later independent mod"); AutoRestore(game);
        assert(File.ReadAllText(nested) == "later independent mod", "repeated restore does not remove newly added unrelated mod");

        game = Game("missing-backup"); dir = Path.GetDirectoryName(game)!;
        var dll = Path.Combine(dir, "version.dll"); File.WriteAllText(dll, "original");
        GameManagement.Begin(game, 0); File.WriteAllText(dll, "installed");
        var ini = Path.Combine(dir, "dlssnr_on_amd.ini"); File.WriteAllText(ini, "installed setting");
        GameManagement.Finish(game, true); File.WriteAllText(dll, "updated"); File.WriteAllText(ini, "edited setting");
        File.Delete(Path.Combine(GameManagement.State(game), "version.dll.backup"));
        Reject(() => AutoRestore(game), "missing original backup blocks automatic restore");
        assert(File.ReadAllText(dll) == "updated" && File.ReadAllText(ini) == "edited setting", "failed preflight leaves all game files unchanged");
        assert(GameManagement.Read(game)!.Phase == "installed", "failed preflight does not mark restored");

        game = Game("record-save-retry"); dir = Path.GetDirectoryName(game)!;
        dll = Path.Combine(dir, "version.dll");
        GameManagement.Begin(game, 0); File.WriteAllText(dll, "installed"); GameManagement.Finish(game, true);
        File.WriteAllText(dll, "updated before restore");
        var blocked = Path.Combine(GameManagement.State(game), "record.tmp"); Directory.CreateDirectory(blocked);
        Reject(() => AutoRestore(game), "record save failure reported");
        assert(GameManagement.Read(game)!.Phase == "installed", "failed record save retains retryable phase");
        Directory.Delete(blocked); AutoRestore(game);
        assert(GameManagement.Read(game)!.Phase == "restored" && !File.Exists(dll), "retry after interrupted completion succeeds");
        assert(Directory.GetFiles(GameManagement.State(game), "version.dll", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "updated before restore"), "retry preserves first conflict archive");

        game = Game("unchanged-no-backup"); dir = Path.GetDirectoryName(game)!;
        dll = Path.Combine(dir, "version.dll"); File.WriteAllText(dll, "existing mod");
        GameManagement.Begin(game, 0); GameManagement.Finish(game, true);
        File.Delete(Path.Combine(GameManagement.State(game), "version.dll.backup"));
        File.WriteAllText(dll, "independent update"); AutoRestore(game);
        assert(File.ReadAllText(dll) == "independent update", "unchanged-at-install file needs no backup and is preserved");

        game = Game("no-record"); Reject(() => AutoRestore(game), "automatic restore cannot assume ownership without record");
        game = Game("interrupted-install"); GameManagement.Begin(game, 0);
        Reject(() => AutoRestore(game), "automatic restore still rejects interrupted external install");
    }
}
