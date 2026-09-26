using AmdNrAssistant;

static class InstallerProtocolTests
{
    public static void Run(Action<bool, string> assert)
    {
        const string exe = @"D:\SteamLibrary\steamapps\common\Draw & Guess\beta\Draw&Guess.exe";
        InstallerProtocol.CheckWarnings("AMD HIP runtime was found.\r\nAnother DLL was not found.");
        assert(true, "unrelated missing DLL does not misclassify AMD runtime");
        try { InstallerProtocol.CheckWarnings("The AMD HIP runtime (amdhip64_7.dll) was not found."); }
        catch (MissingAmdHipRuntimeException e)
        {
            assert(e.Message.Contains("NVIDIA") && e.Message.Contains("amdhip64_7.dll"), "runtime failure explains NVIDIA incompatibility and names dependency");
        }
        const string folder = "Game folder: D:\\SteamLibrary\\steamapps\\common\\Draw & Guess\\beta\\\r\n";
        void Reject(Action action, string name)
        {
            try { action(); } catch (IOException) { assert(true, name); return; }
            throw new Exception("Expected rejection: " + name);
        }
        var p = new InstallerProtocol(exe);
        assert(p.Feed(folder + "Use this fol") == null, "partial prompts receive no input");
        assert(p.Feed("der? [Y/n]") == "y", "matching directory confirmed automatically");
        assert(p.Feed("[1] UnityCrashHandler64.exe 1 MB\r\n[2] Draw&Guess.exe 0 MB\r\nWhich one is the game? Press Enter for [1], or type another number:") == "2",
            "select exact game rather than default crash handler");
        p.EnsureCompleted();
        assert(p.Feed("Unknown confirmation? [Y/n]") == null, "unknown prompt never automatically accepted");
        var fragmented = new InstallerProtocol(exe);
        string? reply = null;
        foreach (var character in folder + "Use this folder? [Y/n]")
        {
            var next = fragmented.Feed(character.ToString());
            if (next != null) { assert(reply == null, "directory gets only one reply"); reply = next; }
        }
        assert(reply == "y", "single-character stdout chunks are supported");
        var warning = new InstallerProtocol(exe);
        assert(warning.Feed("The AMD HIP run") == null, "partial warning receives no confirmation");
        Reject(() => warning.Feed("time (amdhip64_7.dll) was not found."), "fragmented HIP warning still blocks installation");
        var reordered = new InstallerProtocol(exe);
        Reject(() => reordered.Feed("[1] Draw&Guess.exe 0 MB\r\nWhich one is the game? type another number:"), "EXE selection before directory verification rejected");
        Reject(() => new InstallerProtocol(exe).Feed("Game folder: D:\\Other\\\r\nUse this folder? [Y/n]"), "reject wrong directory");
        Reject(() => new InstallerProtocol(exe).Feed("The AMD HIP runtime (amdhip64_7.dll) was not found."), "missing HIP runtime blocks installation");
        Reject(() => new InstallerProtocol(exe).Feed("Install anyway? [y/N]"), "never ignore upstream warning");
        Reject(() => new InstallerProtocol(exe).EnsureCompleted(), "silent or unknown protocol cannot claim success");
        var missing = new InstallerProtocol(exe); missing.Feed(folder + "Use this folder? [Y/n]");
        Reject(() => missing.Feed("[1] UnityCrashHandler64.exe 1 MB\r\nWhich one is the game? type another number:"), "missing selected EXE rejected");
        var duplicate = new InstallerProtocol(exe); duplicate.Feed(folder + "Use this folder? [Y/n]");
        Reject(() => duplicate.Feed("[1] Draw&Guess.exe 0 MB\r\n[2] Draw&Guess.exe 0 MB\r\nWhich one is the game? type another number:"), "ambiguous EXE rejected");
    }
}
