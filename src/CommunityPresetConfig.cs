using Mu.Presets;

namespace AmdNrAssistant;

public static class CommunityPresetConfig
{
    public static bool UsesVulkan(GameRecord record) => record.Mode == 2 &&
        record.Files.Any(f => f.Name.Equals("winmm.dll", StringComparison.OrdinalIgnoreCase));

    public static string FileName(int mode) => mode == 0 ? "dlssnr_on_amd.ini"
        : mode is 2 or 4 or 6 ? "OptiScaler.ini" : throw new IOException("当前方案不能分享。");

    public static PresetSetting[] ReadFromText(int mode, string text)
    {
        var file = FileName(mode);
        var result = new List<PresetSetting>();
        var section = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (ManagedIni.TryReadSection(line, out var header)) { section = header; continue; }
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            var equal = line.IndexOf('=');
            if (equal < 1) continue;
            var setting = new PresetSetting(file, section, line[..equal].Trim(), ManagedIni.Value(line));
            if (PresetRules.Allowed(mode, setting)) result.Add(setting);
        }
        return PresetRules.Validate(mode, result);
    }

    public static (GameRecord Record, PresetSetting[] Settings) Capture(string exe)
    {
        Core.ValidateGame(exe);
        var record = GameManagement.Read(exe);
        if(record?.Mode==5)throw new IOException("32位桥接暂不支持方案上传，现有玩家方案仅适用于对应64位路线。");
        if (record is not { Phase: "installed" } || record.Mode is not (0 or 2 or 4 or 6))
            throw new IOException("请先为这个游戏完成配置，再分享方案。");
        var path = Path.Combine(Path.GetDirectoryName(exe)!, FileName(record.Mode));
        Core.RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
            throw new IOException("没有找到可读取的方案设置文件。");
        var text = File.ReadAllText(path);
        var settings = record.Neural is { } manifest
            ? PresetRules.Validate(6, manifest.ModifiedConfigKeys.Where(k => k.Section == "DlssNr").Select(k => new PresetSetting("OptiScaler.ini", k.Section, k.Key,
                ManagedIni.Get(text, k.Section, k.Key) ?? throw new IOException("缺少神经渲染参数：" + k.Key))))
            : ReadFromText(record.Mode, text);
        var vulkan = UsesVulkan(record);
        if (!PresetRules.VulkanCompatible(record.Mode, vulkan, settings))
            throw new IOException("当前 Vulkan 与帧生成设置不兼容，暂不能分享。");
        return (record, settings);
    }

    public static string Patch(int mode, string original, IEnumerable<PresetSetting> settings)
    {
        var updated = original;
        foreach (var setting in PresetRules.Validate(mode, settings))
            updated = OptiInstaller.SetIni(updated, setting.Section, setting.Key, setting.Value);
        return updated;
    }

    public static void Apply(string exe, PresetDetail preset)
    {
        Core.ValidateGame(exe);
        GameManagement.EnsureClosed(exe);
        var record = GameManagement.Read(exe);
        if (record is not { Phase: "installed" } || record.Mode != preset.Item.Mode)
            throw new IOException("当前游戏安装的方案与分享方案不同，请先切换方案。");
        if (!record.Version.Equals(preset.Item.ComponentVersion, StringComparison.OrdinalIgnoreCase))
            throw new IOException("组件版本不同，不能直接套用这份参数。");
        if (record.Mode == 2 && UsesVulkan(record) != preset.Item.Vulkan)
            throw new IOException("当前游戏图形接口与分享方案不同，请先切换方案。");
        if (!PresetRules.VulkanCompatible(record.Mode, preset.Item.Vulkan, preset.Settings))
            throw new IOException("这份 Vulkan 方案包含不兼容的帧生成设置。");
        var path = Path.Combine(Path.GetDirectoryName(exe)!, FileName(record.Mode));
        Core.RejectLinks(path);
        if (!File.Exists(path)) throw new IOException("游戏缺少方案设置文件。");
        if (record.Neural is { } neural)
        {
            var options = NeuralOptionsFromPreset(preset.Settings, preset.Item.ComponentVersion);
            if (options.Backend != neural.Backend) throw new IOException("分享方案后端不同，请先切换并部署所需 Runtime。");
            var plan = new NeuralPlan(preset.Item.GameKey, neural.Backend, neural.GraphicsApi, neural.Proxy, options.Passes ?? 1,
                "unverified", "community:" + preset.Item.Id, options.NrInline ?? false, options.EarlyAnyModuleWrap ?? false,
                options.AllowEnhancedBarriers ?? false, [], options);
            NeuralRenderInstaller.UpdateSettings(exe, plan); return;
        }
        var original = File.ReadAllText(path);
        var updated = Patch(record.Mode, original, preset.Settings);
        if (updated != original) LivePanel.SaveConfig(exe, path, original, updated);
    }
    public static NeuralOptions NeuralOptionsFromPreset(IEnumerable<PresetSetting> settings, string? componentVersion = null)
    {
        var values = PresetRules.Validate(6, settings).ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        bool Flag(string key, bool fallback) => values.TryGetValue(key, out var value) ? bool.Parse(value) : fallback;
        double Number(string key, double fallback) => values.TryGetValue(key, out var value) ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        var mochi = values["NrBackend"] == "mochizuki";
        var backend = values["NrBackend"];
        if (componentVersion == "AMDNR " + Mu.Neural.NeuralBackends.AmdNrVersion)
        {
            if (backend != "lmxxf") throw new IOException("AMDNR RDNA3 分享方案不能混用其他后端。");
            backend = Mu.Neural.NeuralBackends.LmxxfRdna3;
        }
        return new(backend, Passes: (int)Number(mochi ? "MochizukiPasses" : "Passes", 1),
            ModelScale: Number("MochizukiModelScale", 1), Temporal: Flag("MochizukiTemporal", true),
            HistoryStrength: Number("MochizukiHistoryStrength", 1), ApplyModel: Flag("MochizukiApplyModel", true),
            DynamicResolution: (int)Number("MochizukiDynamicResolution", 1), NrInline: Flag("NrInline", false),
            EarlyAnyModuleWrap: Flag("LmxxfEarlyAnyModuleWrap", false), AllowEnhancedBarriers: Flag("LmxxfAllowEnhancedBarriers", false),
            LmxxfFastMode: backend == Mu.Neural.NeuralBackends.LmxxfRdna3 ? Flag("AmdLmxxfFastMode", true) : null,
            LmxxfTierSnap: backend == Mu.Neural.NeuralBackends.LmxxfRdna3 ? Flag("AmdLmxxfTierSnap", true) : null);
    }
}
