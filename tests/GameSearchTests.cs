using AmdNrAssistant;

static class GameSearchTests
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Game search: " + message);
            count++;
        }
        foreach (var query in new[] { "神力科莎", "Assetto Corsa", "  ASSETTO  CORSA ", "assetto-corsa", "Ａｓｓｅｔｔｏ　Ｃｏｒｓａ", "244210" })
            Check(GameSearch.Matches("assettocorsa", "244210", @"D:\SteamLibrary\steamapps\common\assettocorsa", query), "Steam game missing for " + query);
        Check(GameSearch.Matches("acs", null, @"D:\SteamLibrary\steamapps\common\assettocorsa", "神力科莎"), "manual acs.exe entry lost its Chinese alias");
        Check(GameSearch.Matches("assettocorsa", null, null, "神力科莎"), "directory fallback without Steam manifest lost alias");
        Check(GameSearch.Matches("神力科莎", null, null, "Assetto Corsa"), "Chinese title cannot be found by English name");
        Check(GameSearch.Matches("Assetto Corsa Ultimate Edition", null, null, "神力科莎终极版"), "Ultimate edition shares original game's identity");
        Check(!GameSearch.Matches("assettocorsa-helper", null, null, "神力科莎"), "unrelated helper inherits game identity");
        Check(!GameSearch.Matches("assettocorsa", "42", null, "神力科莎"), "folder overrides a different Steam identity");
        Check(GameSearch.Matches("Assetto Corsa Competizione", "805550", null, "神力科莎 竞速"), "ACC Chinese alias missing");
        Check(!GameSearch.Matches("assettocorsa", "244210", null, "竞速"), "original inherits sequel title");
        Check(GameSearch.Matches("Assetto Corsa EVO", "3058630", null, "神力科莎EVO"), "EVO Chinese alias missing");
        Check(GameSearch.Matches("Assetto Corsa Rally", "3917090", null, "神力科莎拉力"), "Rally Chinese alias missing");
        Check(GameSearch.Matches("Fixture Game", null, null, "fixture game"), "existing title search broken");
        Check(GameSearch.Matches("Fixture Game", null, null, "   "), "blank search hides games");
        Check(!GameSearch.Matches("Fixture Game", null, null, "神力科莎"), "unrelated game appears in results");
        Check(!GameSearch.Terms("acs", null, @"D:\private-user\assettocorsa").Any(t => t.Contains("private-user")), "search terms expose a full local path");
        Console.WriteLine($"Game library search passed: {count} assertions.");
    }
}
