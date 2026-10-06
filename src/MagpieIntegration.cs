using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmdNrAssistant;

// Separate portable installation; never touches game files or a user's existing Magpie.
public static class MagpieIntegration
{
    public const string Tag = "v0.6.8-experimental.1";
    public const string Repository = "https://github.com/SAOG0721/Magpie";
    public const string Sha256 = "efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c";
    public const long PackageSize = 489787536;
    public const string DefaultEffect = "DLSSNR\\DLSSNR_AI_Filter";
    public const string DefaultModeName = "MU · DLSSNR AI Filter";
    public const string MirrorUrl = "https://amd-dlss-mu.claude-api.cn/mirrors/magpie/" + Tag + "/" + Sha256 + "/Magpie-Experimental-x64.zip";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "tools", "Magpie");
    public static string InstallDirectory => Path.Combine(Root, Tag);
    public static string ConfigJson => """
        {
          "language": "zh-cn",
          "shortcuts": { "scale": 3137, "windowedModeScale": 3153 },
          "scalingModes": [
            { "name": "MU · DLSSNR AI Filter", "effects": [{ "name": "DLSSNR\\DLSSNR_AI_Filter" }] },
            { "name": "MU · 通用缩放 Lanczos", "effects": [{ "name": "Lanczos", "scalingType": 1, "scale": { "x": 1, "y": 1 } }] }
          ],
          "profiles": [{ "scalingMode": 0 }]
        }
        """;

    public static string SafeEntryPath(string directory, string entry)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is "." or ".." || p.Contains(':') || p.EndsWith(' ') || p.EndsWith('.')) ||
            entry.StartsWith('/') || entry.StartsWith('\\')) throw new IOException("压缩包路径无效。");
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(directory, Path.Combine(parts)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("压缩包路径越界。");
        Core.RejectLinks(target);
        return target;
    }

    public static async Task<string> PrepareAsync(IProgress<int> download, IProgress<string> phase, CancellationToken token)
    {
        Core.RejectLinks(Root);
        Directory.CreateDirectory(Root);
        // Cross-process lock prevents two launchers from promoting the same installation.
        using var gate = new FileStream(Path.Combine(Root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Directory.Exists(InstallDirectory))
            return await Task.Run(() =>
            {
                var installedExe = ValidateInstallation(InstallDirectory, token);
                EnsureDefaultEffect(Path.GetDirectoryName(installedExe)!, token);
                return installedExe;
            }, token);
        var archive = Path.Combine(Root, Tag + ".zip");
        Core.RejectLinks(archive);
        var cached = File.Exists(archive) && new FileInfo(archive).Length == PackageSize &&
            await Task.Run(() => Core.Hash(archive) == Sha256, token);
        if (!cached)
        {
            phase.Report("正在从 MU 镜像下载大力喜鹊完整包（约 467 MiB）…");
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadSources.DownloadAsync(client, Repository + "/releases/download/" + Tag + "/Magpie-Experimental-x64.zip",
                "https://api.github.com/repos/SAOG0721/Magpie/releases/assets/563188091", Sha256, PackageSize, archive, download, token,
                phase.Report, mirror: MirrorUrl);
        }
        phase.Report("正在解压并初始化独立配置…");
        var stage = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(archive);
                if (zip.Entries.Count > 30000) throw new IOException("压缩包文件数量异常。");
                long total = 0;
                foreach (var item in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("压缩包包含链接。");
                    total = checked(total + item.Length);
                    if (total > 6L * 1024 * 1024 * 1024) throw new IOException("解压大小超出限制。");
                    var target = SafeEntryPath(stage, item.FullName);
                    if (item.FullName.EndsWith('/') || item.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    item.ExtractToFile(target, false);
                }
                var executables = Directory.GetFiles(stage, "Magpie.exe", SearchOption.AllDirectories);
                if (executables.Length != 1) throw new IOException("完整包中未找到唯一 Magpie.exe。");
                var exe = executables[0];
                Core.CheckPe(exe, false);
                var app = Path.GetDirectoryName(exe)!;
                if (!File.Exists(Path.Combine(app, "resources.pri")) || !File.Exists(Path.Combine(app, "effects", "Lanczos.hlsl")))
                    throw new IOException("Magpie 完整包缺少资源或 Lanczos 效果文件。");
                var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
                    .ToDictionary(p => Path.GetRelativePath(stage, p), p => { token.ThrowIfCancellationRequested(); return Core.Hash(p); });
                File.WriteAllText(Path.Combine(stage, "mu-package.json"), JsonSerializer.Serialize(files));
                var config = Path.Combine(app, "config", "v4e", "config.json");
                if (File.Exists(config)) throw new IOException("上游包含预置配置，请先审核后接入。");
                Directory.CreateDirectory(Path.GetDirectoryName(config)!);
                File.WriteAllText(config, ConfigJson);
                EnsureDefaultEffect(app, token);
                token.ThrowIfCancellationRequested();
                Directory.Move(stage, InstallDirectory);
            }, token);
            return await Task.Run(() => ValidateInstallation(InstallDirectory, token), token);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    // One-time migration: preserve existing effect groups and custom profiles.
    // The separate marker survives upstream rewriting its JSON (unknown keys are dropped).
    public static void EnsureDefaultEffect(string appDirectory, CancellationToken token)
    {
        var effect = Path.Combine(appDirectory, "effects", "DLSSNR", "DLSSNR_AI_Filter.hlsl");
        Core.RejectLinks(effect);
        if (!File.Exists(effect)) throw new IOException("大力喜鹊缺少 DLSSNR AI Filter 效果，未启动；不会静默改用缩放。");
        var config = Path.Combine(appDirectory, "config", "v4e", "config.json");
        var marker = Path.Combine(appDirectory, "config", "v4e", "mu-dlssnr-default-v1.json");
        Core.RejectLinks(config); Core.RejectLinks(marker);
        if (File.Exists(marker)) return;
        token.ThrowIfCancellationRequested();
        var original = File.ReadAllText(config);
        JsonObject data;
        try { data = JsonNode.Parse(original) as JsonObject ?? throw new IOException("大力喜鹊配置格式无效。"); }
        catch (JsonException e) { throw new IOException("大力喜鹊配置无法读取，未覆盖原配置。", e); }
        if (data["scalingModes"] is not JsonArray modes || data["profiles"] is not JsonArray profiles ||
            profiles.Count == 0 || profiles[0] is not JsonObject defaultProfile)
            throw new IOException("大力喜鹊配置结构不匹配，未覆盖原配置。");
        var index = -1;
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i] is JsonObject mode && mode["name"]?.GetValue<string>() == DefaultModeName &&
                mode["effects"] is JsonArray effects && effects.Count == 1 &&
                effects[0]?["name"]?.GetValue<string>() == DefaultEffect) { index = i; break; }
        }
        if (index < 0)
        {
            index = modes.Count;
            modes.Add(JsonNode.Parse("""{"name":"MU · DLSSNR AI Filter","effects":[{"name":"DLSSNR\\DLSSNR_AI_Filter"}]}"""));
        }
        defaultProfile["scalingMode"] = index;
        var next = data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temporary = config + ".mu-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, next);
            token.ThrowIfCancellationRequested();
            if (File.ReadAllText(config) != original) throw new IOException("大力喜鹊配置被其他程序修改，请退出 Magpie 后重试。");
            var backup = config + ".before-ai-filter-" + Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(config, backup, false);
            File.Move(temporary, config, true);
            File.WriteAllText(marker, "{\"defaultEffect\":\"DLSSNR\\\\DLSSNR_AI_Filter\",\"version\":1}");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string ValidateInstallation(string directory, CancellationToken token)
    {
        Core.RejectLinks(directory);
        var receipt = Path.Combine(directory, "mu-package.json");
        Core.RejectLinks(receipt);
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(receipt))
            ?? throw new IOException("大力喜鹊安装记录无效。");
        string? exe = null;
        foreach (var (name, hash) in files)
        {
            token.ThrowIfCancellationRequested();
            var path = SafeEntryPath(directory, name);
            if (!File.Exists(path) || !string.Equals(Core.Hash(path), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("大力喜鹊组件缺失或发生变化：" + name + "。未启动，请保留现场后重新安装。");
            if (Path.GetFileName(path).Equals("Magpie.exe", StringComparison.OrdinalIgnoreCase)) exe = path;
        }
        return exe ?? throw new IOException("大力喜鹊主程序缺失。");
    }
}
