using System.Text.Json.Nodes;

namespace AmdNrAssistant;

// Migrate only MU's known presets. Profile indices refer to the original array,
// so every surviving profile must be remapped when a preset is removed.
public static class AmdMagpieEffectGroups
{
    public const string Name = "MU-AMD-DLSS5";
    public static readonly string[] Effects =
    ["FSR3\\FSR3_SR", "FSR4\\FSR4_SR", "XeSSFG\\XeSS_FrameGeneration_x2_ZeroMV"];

    public static JsonObject Migrate(JsonObject original)
    {
        var data = (JsonObject)original.DeepClone();
        if (data["scalingModes"] is not JsonArray modes || data["profiles"] is not JsonArray profiles || profiles.Count == 0)
            throw new IOException("AMD 喜鹊配置结构不匹配，未修改效果组。");
        var kept = new JsonArray();
        var remap = new Dictionary<int, int>();
        var removed = new List<int>();
        var target = -1;
        for (var i = 0; i < modes.Count; i++)
        {
            if (modes[i] is not JsonObject mode)
                throw new IOException("AMD 喜鹊存在无效效果组，未修改配置。");
            var name = mode["name"]?.GetValue<string>();
            var effects = mode["effects"] as JsonArray;
            // A renamed or edited group belongs to the user, even when its title
            // matches an old MU preset. Only the original single-effect chains go.
            var oldPreset = effects?.Count == 1 &&
                (effects[0]?["parameters"] is null || effects[0]?["parameters"] is JsonObject { Count: 0 }) &&
                ((name == "MU · DLSSNR AI Filter" && effects[0]?["name"]?.GetValue<string>() == "DLSSNR\\DLSSNR_AI_Filter") ||
                 (name == "MU · 通用缩放 Lanczos" && effects[0]?["name"]?.GetValue<string>() == "Lanczos"));
            if (oldPreset) { removed.Add(i); continue; }
            if (name == Name)
            {
                if (target >= 0 || effects?.Count != Effects.Length ||
                    !effects.Select(e => e?["name"]?.GetValue<string>()).SequenceEqual(Effects))
                    throw new IOException("已有 MU-AMD-DLSS5 效果组与预设不同，请先重命名该自定义组；原配置未修改。");
                target = kept.Count;
            }
            remap[i] = kept.Count;
            kept.Add(mode.DeepClone());
        }
        if (target < 0)
        {
            target = kept.Count;
            var effects = new JsonArray();
            foreach (var effect in Effects) effects.Add(new JsonObject { ["name"] = effect });
            kept.Add(new JsonObject { ["name"] = Name, ["effects"] = effects });
        }
        foreach (var old in removed) remap[old] = target;
        foreach (var node in profiles)
        {
            if (node is not JsonObject profile)
                throw new IOException("AMD 喜鹊存在无效游戏配置，未修改效果组。");
            // Magpie's default scalingMode is zero when omitted; -1 means none.
            var oldIndex = profile["scalingMode"]?.GetValue<int>() ?? 0;
            if (oldIndex == -1) continue;
            if (!remap.TryGetValue(oldIndex, out var newIndex))
                throw new IOException("AMD 喜鹊配置引用了不存在的效果组，未修改原配置。");
            profile["scalingMode"] = newIndex;
        }
        data["scalingModes"] = kept;
        return data;
    }
}
