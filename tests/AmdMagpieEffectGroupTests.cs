using AmdNrAssistant;
using System.Text.Json.Nodes;

internal static class AmdMagpieEffectGroupTests
{
    public static void Run()
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        var original = JsonNode.Parse("""
        {"language":"en-us","scalingModes":[
          {"name":"MU · DLSSNR AI Filter","effects":[{"name":"DLSSNR\\DLSSNR_AI_Filter"}]},
          {"name":"我的效果","effects":[{"name":"CAS\\CAS","parameters":{"sharpness":0.4}}]},
          {"name":"MU · 通用缩放 Lanczos","effects":[{"name":"Lanczos","scalingType":1,"scale":{"x":1,"y":1}}]}],
        "profiles":[{"scalingMode":0},{"scalingMode":1,"path":"game.exe"},{"scalingMode":2},{"scalingMode":-1}]}
        """)!.AsObject();
        var saved = original.ToJsonString();
        var migrated = AmdMagpieEffectGroups.Migrate(original);
        var modes = migrated["scalingModes"]!.AsArray(); var profiles = migrated["profiles"]!.AsArray();
        Check(original.ToJsonString() == saved, "migration mutated the source before save");
        Check(modes.Count == 2 && modes[1]!["name"]!.GetValue<string>() == "MU-AMD-DLSS5", "old presets remain or new preset missing");
        Check(modes[1]!["effects"]!.AsArray().Select(e => e!["name"]!.GetValue<string>()).SequenceEqual(AmdMagpieEffectGroups.Effects), "chain order differs from requested preset");
        Check(JsonNode.DeepEquals(modes[0], original["scalingModes"]![1]), "custom group or parameters changed");
        Check(profiles.Select(p => p!["scalingMode"]!.GetValue<int>()).SequenceEqual(new[] {1,0,1,-1}), "profiles now point to the wrong effects");
        Check(migrated["language"]!.GetValue<string>() == "en-us" && profiles[1]!["path"]!.GetValue<string>() == "game.exe", "unrelated user settings changed");
        Check(JsonNode.DeepEquals(migrated, AmdMagpieEffectGroups.Migrate(migrated)), "repeated launch duplicates or rewrites groups");
        var reset = original.DeepClone().AsObject();reset["scalingModes"] = new JsonArray(original["scalingModes"]![0]!.DeepClone());reset["profiles"] = new JsonArray(new JsonObject());
        Check(AmdMagpieEffectGroups.Migrate(reset)["profiles"]![0]!["scalingMode"]!.GetValue<int>() == 0, "reset/default profile was not repaired");
        var conflict = migrated.DeepClone().AsObject();conflict["scalingModes"]![1]!["effects"]![0]!["name"] = "Lanczos";
        var conflictBefore = conflict.ToJsonString();
        try { AmdMagpieEffectGroups.Migrate(conflict);throw new Exception("custom preset collision overwritten"); }
        catch (IOException) { Check(conflict.ToJsonString() == conflictBefore, "conflicting custom preset modified"); }
        var root = Path.Combine(Path.GetTempPath(), "mu-amd-group-"+Guid.NewGuid().ToString("N"));
        try
        {
            var config = Path.Combine(root,"config","v4e","config.json"); Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config,saved);
            try { AmdMagpieIntegration.EnsureAmdEffectGroup(root,default);throw new Exception("missing effects accepted"); }
            catch(IOException) { Check(File.ReadAllText(config)==saved,"missing effects changed configuration"); }
            foreach(var effect in AmdMagpieEffectGroups.Effects)
            {
                var path=Path.Combine(root,"effects",effect.Replace('\\',Path.DirectorySeparatorChar)+".hlsl");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,"test fixture");
            }
            AmdMagpieIntegration.EnsureAmdEffectGroup(root,default);
            Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(config)),migrated),"saved configuration differs from migration");
            Check(Directory.GetFiles(Path.GetDirectoryName(config)!,"*.bak").Length==1,"original configuration not backed up");
            AmdMagpieIntegration.EnsureAmdEffectGroup(root,default);
            Check(Directory.GetFiles(Path.GetDirectoryName(config)!,"*.bak").Length==1,"repeat launch rewrote unchanged config");
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        Console.WriteLine($"AMD effect group migration: {count} assertions passed.");
    }
}
