using System.Text.RegularExpressions;
using Mu.Compatibility;

namespace AmdNrAssistant;

public enum SmartRenderGoal { Quality, Performance, Custom }
public enum SmartRenderAvailability { Ready, HardwareUnsupported, HardwareUnconfirmed, RequirementsMissing }

public sealed record SmartRenderRecommendation(int QualityMode, string QualityReason,
    int PerformanceMode, string PerformanceReason, string HardwareLabel, string[] Notices,
    SmartRenderAvailability Availability);

public sealed class SmartRenderSelection
{
    public SmartRenderRecommendation? Recommendation { get; private set; }
    public SmartRenderGoal? Goal { get; private set; }
    public int? Mode { get; private set; }
    public bool ManualWithoutDetection { get; private set; }
    public bool CanConfigure => Mode.HasValue && (Recommendation != null || ManualWithoutDetection);
    public bool ShowSchemeList => Goal == SmartRenderGoal.Custom;

    public void Reset()
    {
        Recommendation = null; Goal = null; Mode = null; ManualWithoutDetection = false;
    }
    public void SetRecommendation(SmartRenderRecommendation recommendation, SmartRenderGoal initialGoal)
    {
        Reset();
        Recommendation = recommendation;
        SelectGoal(initialGoal);
    }
    public void UseManual()
    {
        Reset(); ManualWithoutDetection = true; Goal = SmartRenderGoal.Custom; Mode = 2;
    }
    public bool SelectGoal(SmartRenderGoal goal)
    {
        if (goal == SmartRenderGoal.Custom)
        {
            if (Recommendation == null && !ManualWithoutDetection) return false;
            Goal = goal; Mode ??= 2; return true;
        }
        if (Recommendation == null) return false;
        var mode = SmartRenderAdvisor.RecommendedMode(goal, Recommendation);
        if (mode == 2) return false;
        Goal = goal; Mode = mode; return true;
    }
    public bool IsModeAvailable(int mode) => mode switch
    {
        2 => Recommendation != null || ManualWithoutDetection,
        0 => ManualWithoutDetection || Recommendation?.QualityMode == 0,
        4 => ManualWithoutDetection || Recommendation?.PerformanceMode == 4 || Recommendation?.QualityMode == 4,
        _ => false
    };
    public bool SelectMode(int mode)
    {
        if (!ShowSchemeList || !IsModeAvailable(mode)) return false;
        Mode = mode; return true;
    }
}

public static class SmartRenderAdvisor
{
    // AMD publishes RX 7700 XT (12 GB) and the named RX 7900 XT/XTX/GRE models
    // above the 8 GB recommendation threshold.
    // Use the model only when dxdiag did not report dedicated memory; never replace a measured value.
    public static bool HasKnownHighVramModel(string name) =>
        Regex.IsMatch(name, @"\bRX\s*(?:7700\s*XT|7900\s*(?:XTX|XT|GRE))\b", RegexOptions.IgnoreCase);

    // Prefer a discrete RX card over an integrated adapter when dxdiag lists both.
    // This is still only a recommendation; the game may use another adapter.
    public static CompatibilityGpu? SelectGpu(IReadOnlyList<CompatibilityGpu> gpus) =>
        gpus.OrderByDescending(g => g.Vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => Regex.IsMatch(g.Name, @"\bRX\s*[6-9]\d{3}\b", RegexOptions.IgnoreCase))
            .ThenByDescending(g => g.VramMb ?? 0)
            .FirstOrDefault();

    public static int RecommendedMode(SmartRenderGoal goal, SmartRenderRecommendation recommendation) =>
        goal == SmartRenderGoal.Performance ? recommendation.PerformanceMode : recommendation.QualityMode;

    // These are broad defaults, not measured performance or a GPU support list.
    public static SmartRenderRecommendation Recommend(CompatibilityGpu? gpu, bool neuralInstallAllowed)
        => Recommend(gpu, neuralInstallAllowed, neuralInstallAllowed);

    public static SmartRenderRecommendation Recommend(CompatibilityGpu? gpu, bool qualityInstallAllowed, bool combinedInstallAllowed)
    {
        var name = gpu?.Name ?? CompatibilityEnvironmentReader.Unknown;
        var known = name != CompatibilityEnvironmentReader.Unknown;
        var amd = known && (gpu?.Vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase) == true ||
                            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
        var series = Regex.Match(name, @"\bRX\s*(\d{4})\b", RegexOptions.IgnoreCase);
        var generation = series.Success ? int.Parse(series.Groups[1].Value) / 1000 : 0;
        var vram = gpu?.VramMb;
        var modelCapacityFallback = amd && (vram is null or <= 0) && HasKnownHighVramModel(name);
        var highCapacity = vram >= 8 * 1024 || modelCapacityFallback;
        var modelEligible = amd && generation is >= 6 and <= 9;
        var availability = known && (!modelEligible || vram is > 0 and < 8 * 1024)
            ? SmartRenderAvailability.HardwareUnsupported
            : !known || (vram is null or <= 0) && !modelCapacityFallback
                ? SmartRenderAvailability.HardwareUnconfirmed
                : qualityInstallAllowed || combinedInstallAllowed
                    ? SmartRenderAvailability.Ready
                    : SmartRenderAvailability.RequirementsMissing;
        var qualityCandidate = amd && generation is >= 6 and <= 9 && highCapacity && qualityInstallAllowed;
        var combinedCandidate = amd && generation is >= 6 and <= 9 && highCapacity && combinedInstallAllowed;
        var label = known ? name + (vram is > 0 ? $" · {vram / 1024d:0.#} GB" : "") : "显卡信息未读到";
        var notices = new List<string>();
        if (modelCapacityFallback) notices.Add("显存未读到；仅根据 AMD 官方公布的明确型号估计容量，实际显存仍需确认。");
        else if (!known || vram is null or <= 0) notices.Add("部分显卡信息未读到，已采用保守推荐。");
        if (!qualityInstallAllowed && !combinedInstallAllowed) notices.Add("神经渲染安装条件未通过，可在一键排查中查看原因。");
        notices.Add("游戏的 DLSS / XeSS 入口与实际帧生成状态需进游戏确认。");
        var fallbackReason = availability switch
        {
            SmartRenderAvailability.HardwareUnsupported => "当前检测到的显卡不支持开启 DLSS 5，可使用标准 OptiScaler。",
            SmartRenderAvailability.HardwareUnconfirmed => "未能确认显卡型号或显存，暂不推荐开启 DLSS 5。",
            _ => "神经渲染所需组件或游戏基础条件未通过，暂时无法开启 DLSS 5。"
        };
        var qualityMode = qualityCandidate ? 0 : 2;
        return new(qualityMode,
            qualityCandidate ? "已匹配模式一的基础条件；游戏接口和画质仍需进游戏验证。" : fallbackReason,
            combinedCandidate ? 4 : 2,
            combinedCandidate ? "已匹配 DLSS 5 + XeFG 的基础条件；帧生成效果需进游戏验证。" : fallbackReason,
            label, notices.ToArray(), availability);
    }
}

public sealed record SmartCheck(string Title, string Detail, bool Problem, bool Unknown = false);

public static class SmartRenderDiagnostics
{
    public static SmartCheck[] Check(string exe)
    {
        var items = new List<SmartCheck>();
        var record = GameManagement.Read(exe);
        var directory = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        var hardware = GameManagement.Hardware();
        var amd = hardware.Any(x => x.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
        items.Add(new("显卡", amd ? "已识别到 AMD Radeon 显卡。" :
            "未能确认游戏使用的是 AMD Radeon 显卡。", !amd, !amd));
        items.Add(new("驱动", hardware.Any(x => x.Contains("Driver ", StringComparison.OrdinalIgnoreCase)) ?
            "已读到显卡驱动；具体 Adrenalin 版本仍需在 AMD 软件中核对。" : "未读到显卡驱动版本，请检查驱动安装。",
            false, true));
        if (record is not { Phase: "installed" })
        {
            items.Add(new("安装记录", "没有找到完整的 MU 安装记录，请先完成一键配置。", true));
            return items.ToArray();
        }
        items.Add(new("安装记录", "已找到这款游戏的安装记录。", false));
        if (record.Mode is 2 or 4)
        {
            var changed = new List<string>();
            foreach (var file in record.Files.Where(f => f.Before != f.After))
            {
                try
                {
                    var path = Path.Combine(directory, file.Name);
                    Core.RejectLinks(path);
                    if (!File.Exists(path) || !string.Equals(Core.Hash(path), file.After, StringComparison.OrdinalIgnoreCase))
                        changed.Add(file.Name);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { changed.Add(file.Name); }
            }
            items.Add(new("安装文件完整性", changed.Count == 0 ? "MU 安装的文件与记录一致。" :
                "以下文件缺失或已变化：" + string.Join("、", changed.Take(3)) + (changed.Count > 3 ? $" 等 {changed.Count} 个文件" : ""), changed.Count > 0));
        }
        if (record.Mode == 0)
        {
            var rx6000 = GameManagement.HasRx6000(hardware);
            var hip = rx6000 ? HipRuntime.HasVersion72() : HasHipRuntime();
            items.Add(new("AMD HIP", hip ? (rx6000 ? "已找到 AMD HIP 7.2。" : "已找到 AMD HIP 运行组件。") :
                rx6000 ? "未找到 AMD HIP 7.2，请在一键配置时完成安装。" :
                "未找到 AMD HIP 运行组件；神经渲染可能无法启动，请安装对应版本。", !hip));
            foreach (var (name, label) in new[] { ("nvngx_dlssnr.dll", "神经渲染组件"),
                ("dlssnr_on_amd.ini", "方案设置"), ("dlssnr_on_amd_weights.bin", "模型数据") })
                items.Add(new(label, File.Exists(Path.Combine(directory, name)) ? "已找到。" : "缺失，建议恢复后重新配置。",
                    !File.Exists(Path.Combine(directory, name))));
            if (File.Exists(Path.Combine(directory, Core.DllName)))
            {
                try
                {
                    var dll = Path.Combine(directory, Core.DllName);
                    Core.ValidateDll(dll);
                    var correct = string.Equals(Core.Hash(dll), Core.BundledDllSha256, StringComparison.OrdinalIgnoreCase);
                    items.Add(new("神经渲染版本", correct ? "组件版本符合当前安装要求。" :
                        "组件与 MU 内置文件不一致，建议恢复后重新配置。", !correct));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { items.Add(new("神经渲染版本", "组件无法验证，建议退出游戏后重新排查。", true)); }
            }
        }
        else if (record.Mode == 2)
        {
            foreach (var (name, label) in new[] { ("OptiScaler.ini", "方案设置"),
                ("libxess_fg.dll", "XeFG 组件"), ("amd_fidelityfx_framegeneration_dx12.dll", "FSR 帧生成组件") })
                items.Add(new(label, File.Exists(Path.Combine(directory, name)) ? "已找到。" : "缺失，建议恢复后重新配置。",
                    !File.Exists(Path.Combine(directory, name))));
        }
        if (record.Mode == 4)
        {
            foreach (var (name, label) in new[] { ("dxgi.dll", "帧率方案加载组件"),
                ("OptiScaler.ini", "方案设置"),
                ("OptiScaler/libxess_fg.dll", "XeFG 组件") })
                items.Add(new(label, File.Exists(Path.Combine(directory, name)) ? "已找到。" : "缺失，建议恢复后重新配置。",
                    !File.Exists(Path.Combine(directory, name))));
            var rx6000 = GameManagement.HasRx6000(hardware);
            var hip = rx6000 ? HipRuntime.HasVersion72() : HasHipRuntime();
            items.Add(new("AMD HIP", hip ? (rx6000 ? "已找到 AMD HIP 7.2。" : "已找到 AMD HIP 运行组件。") :
                rx6000 ? "未找到 AMD HIP 7.2，请在一键配置时完成安装。" :
                "未找到 AMD HIP 运行组件，请安装与显卡匹配的运行时。", !hip));
            foreach (var (name, label) in new[] { (Core.DllName, "神经渲染模型组件"),
                ("dlssnr_amd_pass1.dll", "神经渲染运行组件 1"),
                ("dlssnr_amd_pass2.dll", "神经渲染运行组件 2"),
                ("dlssnr_amd_pass3.dll", "神经渲染运行组件 3"),
                ("dlssnr_on_amd_weights.bin", "模型数据") })
                items.Add(new(label, File.Exists(Path.Combine(directory, name)) ? "已找到。" : "缺失，建议恢复后重新配置。",
                    !File.Exists(Path.Combine(directory, name))));
            if (File.Exists(Path.Combine(directory, Core.DllName)))
            {
                try
                {
                    var correct = string.Equals(Core.Hash(Path.Combine(directory, Core.DllName)), Core.BundledDllSha256, StringComparison.OrdinalIgnoreCase);
                    items.Add(new("神经渲染模型版本", correct ? "组件版本符合当前安装要求。" : "组件不匹配，建议恢复后重新配置。", !correct));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { items.Add(new("神经渲染模型版本", "组件无法读取，建议退出游戏后重新排查。", true)); }
            }
        }
        var proxy = GameManagement.Loaders.Any(name => File.Exists(Path.Combine(directory, name)));
        items.Add(new("游戏加载组件", proxy ? "已找到游戏目录中的加载文件；是否被游戏加载需启动后验证。" :
            "游戏目录没有找到加载文件，建议恢复后重新配置。", !proxy, proxy));
        var evidence = Directory.EnumerateFiles(directory).Select(Path.GetFileName).Where(n => n != null &&
            (n.Contains("fsr", StringComparison.OrdinalIgnoreCase) || n.Contains("dlss", StringComparison.OrdinalIgnoreCase))).Any();
        items.Add(new("游戏图形接口", evidence ? "找到图形组件线索，但游戏是否使用它仍需进游戏验证。" :
            "尚未确认游戏使用的接口与帧生成功能，请进游戏验证。", false, true));
        return items.ToArray();
    }

    public static bool HasHipRuntime()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (HipRuntime.HasVersion72()) return true;
        var locations = new List<string> { Environment.SystemDirectory };
        locations.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var key in new[] { "HIP_PATH", "HIP_PATH_64", "ROCM_PATH" })
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } path)
            { locations.Add(path); locations.Add(Path.Combine(path, "bin")); }
        foreach (var directory in locations.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            try { if (File.Exists(Path.Combine(directory, "amdhip64_7.dll"))) return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return false;
    }
}
