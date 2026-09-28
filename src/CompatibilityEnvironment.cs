using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Mu.Compatibility;

namespace AmdNrAssistant;

public sealed record CompatibilitySystemSnapshot(string OsVersion, string SystemDirectX, IReadOnlyList<CompatibilityGpu> Gpus);

public sealed class CompatibilityEnvironmentReader
{
    public const string Unknown = "unknown";
    static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(25);
    readonly string toolVersion;
    CompatibilitySystemSnapshot? system;
    string? gpuWarning;
    public string? Warning { get; private set; }

    public CompatibilityEnvironmentReader(string? toolVersion = null) =>
        this.toolVersion = toolVersion == null ? ReadToolVersion() : CleanVersion(toolVersion, 100);

    public async Task<IReadOnlyList<CompatibilityGpu>> ReadGpusAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Warning = gpuWarning = null;
        system = null;
        if (!OperatingSystem.IsWindows())
        {
            Warning = gpuWarning = "当前系统无法自动识别 Windows 显卡环境；未知字段可手动补充。";
            return [UnknownGpu()];
        }

        string output = Path.Combine(Path.GetTempPath(), "mu-compatibility-" + Guid.NewGuid().ToString("N") + ".xml");
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(DiagnosticTimeout);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dxdiag.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            start.ArgumentList.Add("/whql:off");
            start.ArgumentList.Add("/x");
            start.ArgumentList.Add(output);
            process = Process.Start(start) ?? throw new IOException("Unable to start system diagnostics.");
            await process.WaitForExitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (!File.Exists(output) || new FileInfo(output).Length > 4 * 1024 * 1024)
                throw new IOException("System diagnostics did not produce a usable report.");
            system = ParseDxDiag(await File.ReadAllTextAsync(output, timeout.Token));
            if (system.Gpus.Count == 0)
            {
                Warning = gpuWarning = "未识别到显卡；请选择或填写实际用于游戏的显卡。";
                return [UnknownGpu()];
            }
            if (system.Gpus.Any(g => g.DriverVersion == Unknown || g.VramMb == null))
                Warning = gpuWarning = "部分显卡字段未能读取；未知值不会推断为已验证。";
            return system.Gpus;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Warning = gpuWarning = "显卡检测超时；可以手动补充环境后提交。";
            return [UnknownGpu()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Warning = gpuWarning = "显卡自动检测失败；可以手动补充环境后提交。";
            return [UnknownGpu()];
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await process.WaitForExitAsync(cleanup.Token);
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException or NotSupportedException) { }
                process.Dispose();
            }
            try { File.Delete(output); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public Task<CompatibilityEnvironment> ReadAsync(string exePath, string? installDirectory, CompatibilityGpu gpu, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        token.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var warnings = new List<string>();
            if (gpuWarning != null) warnings.Add(gpuWarning);
            var directories = new List<string>();
            string gameVersion = Unknown;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var fullPath = Path.GetFullPath(exePath);
                    var directory = Path.GetDirectoryName(fullPath)!;
                    directories.Add(directory);
                    gameVersion = ReadFileVersion(fullPath);
                    string build = ReadSteamBuild(directory, token);
                    gameVersion = FormatGameVersion(gameVersion, build);
                }
                if (!string.IsNullOrWhiteSpace(installDirectory) && Directory.Exists(installDirectory))
                    directories.Add(Path.GetFullPath(installDirectory));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                warnings.Add("部分游戏文件无法读取。");
            }

            string dlss = ReadRuntimeVersion(directories, "nvngx_dlss.dll", token);
            string dlss5 = ReadRuntimeVersion(directories, "nvngx_dlssnr.dll", token);
            if (gameVersion == Unknown) warnings.Add("未读取到游戏版本。");
            if (dlss == Unknown || dlss5 == Unknown) warnings.Add("部分 DLSS 文件版本未读取到；文件存在不代表游戏已加载或功能已生效。");
            warnings.Add("渲染 API、游戏设置及其他 Mod 需按实际测试补充；系统 DirectX 版本不代表游戏正在使用的 API。");
            Warning = string.Join(" ", warnings);
            token.ThrowIfCancellationRequested();
            return new CompatibilityEnvironment(gpu,
                system?.OsVersion is { } os && os != Unknown ? os : CleanText(RuntimeInformation.OSDescription),
                system?.SystemDirectX ?? Unknown, gameVersion, Unknown, dlss, dlss5, toolVersion, Unknown, Unknown);
        }, token);
    }

    public static CompatibilitySystemSnapshot ParseDxDiag(string xml)
    {
        using var source = new StringReader(xml);
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        var information = document.Root?.Element("SystemInformation");
        var devices = (document.Root?.Element("DisplayDevices")?.Elements("DisplayDevice") ?? [])
            .Concat(document.Root?.Element("RenderDevices")?.Elements("RenderDevice") ?? []);
        var gpus = devices
            .Select(device => new CompatibilityGpu(
                CleanText(device.Element("CardName")?.Value, 160),
                Vendor(device.Element("Manufacturer")?.Value, device.Element("VendorID")?.Value),
                ParseMemoryMb(device.Element("DedicatedMemory")?.Value), Unknown,
                CleanVersion(device.Element("DriverVersion")?.Value)))
            .Where(gpu => gpu.Name != Unknown)
            .Distinct().ToArray();
        return new(CleanText(information?.Element("OperatingSystem")?.Value),
            CleanDirectX(information?.Element("DirectXVersion")?.Value), gpus);
    }

    public static long? ParseMemoryMb(string? memory)
    {
        // dxdiag DisplayMemory includes shared RAM; only DedicatedMemory belongs here.
        if (string.IsNullOrWhiteSpace(memory)) return null;
        var match = Regex.Match(memory.Trim(), @"^(\d+(?:[.,]\d{3})*(?:[.,]\d+)?)\s*(MB|GB|KB|Bytes?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        string number = match.Groups[1].Value;
        if (Regex.IsMatch(number, @"^\d{1,3}([.,]\d{3})+$")) number = number.Replace(",", "").Replace(".", "");
        else number = number.Replace(',', '.');
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) return null;
        decimal mb = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "GB" => amount * 1024, "KB" => amount / 1024, "BYTE" or "BYTES" => amount / (1024 * 1024), _ => amount
        };
        return mb is > 0 and <= 1024 * 1024 ? (long)Math.Round(mb) : null;
    }

    public static string ParseSteamBuild(string manifest, string installDirectoryName)
    {
        // Only read the top-level AppState values, so similarly named nested fields cannot win.
        var tokens = Regex.Matches(manifest, "\"((?:\\\\.|[^\"\\\\])*)\"|[{}]");
        int depth = 0;
        string? directory = null, build = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            string value = tokens[i].Value;
            if (value == "{") { depth++; continue; }
            if (value == "}") { depth--; continue; }
            if (depth != 1 || i + 1 >= tokens.Count || tokens[i + 1].Value is "{" or "}") continue;
            string key = tokens[i].Groups[1].Value;
            string field = tokens[++i].Groups[1].Value;
            if (key.Equals("installdir", StringComparison.OrdinalIgnoreCase)) directory = field;
            if (key.Equals("buildid", StringComparison.OrdinalIgnoreCase)) build = field;
        }
        return string.Equals(directory, installDirectoryName, StringComparison.OrdinalIgnoreCase)
            && build != null && Regex.IsMatch(build, @"^\d{1,20}$") ? build : Unknown;
    }

    static CompatibilityGpu UnknownGpu() => new(Unknown, Unknown, null, Unknown, Unknown);

    static string Vendor(string? manufacturer, string? id)
    {
        string text = manufacturer ?? "";
        if (id?.Equals("0x1002", StringComparison.OrdinalIgnoreCase) == true || text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (id?.Equals("0x10de", StringComparison.OrdinalIgnoreCase) == true || text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (id?.Equals("0x8086", StringComparison.OrdinalIgnoreCase) == true || text.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        return Unknown;
    }

    static string CleanText(string? value, int maximumLength = 200)
    {
        string text = value?.Trim() ?? "";
        return text.Length > 0 && text.Length <= maximumLength && !text.Any(char.IsControl) && text.IndexOfAny(['\\', '/', '@']) < 0 ? text : Unknown;
    }

    static string CleanVersion(string? value, int maximumLength = 64)
    {
        string text = value?.Trim() ?? "";
        return Regex.IsMatch(text, @"^\d+(?:[.,]\s*\d+){0,5}(?:-[A-Za-z0-9.-]+)?(?:\+[A-Za-z0-9.-]+)?$") && text.Length <= maximumLength ? text : Unknown;
    }

    static string ReadToolVersion()
    {
        var assembly = typeof(CompatibilityEnvironmentReader).Assembly;
        string version = CleanVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, 100);
        return version != Unknown ? version : CleanVersion(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version, 100);
    }

    internal static string FormatGameVersion(string fileVersion, string steamBuild)
    {
        if (steamBuild == Unknown) return fileVersion.Length <= 100 ? fileVersion : Unknown;
        string buildVersion = "Steam build " + steamBuild;
        if (buildVersion.Length > 100) return Unknown;
        string combined = fileVersion == Unknown ? buildVersion : fileVersion + " (" + buildVersion + ")";
        // Preserve a complete measured identifier instead of truncating version digits.
        return combined.Length <= 100 ? combined : buildVersion;
    }

    static string CleanDirectX(string? value) => value != null && Regex.IsMatch(value, @"^DirectX\s+\d+(?:\.\d+)?(?:[a-z])?$", RegexOptions.IgnoreCase)
        ? value.Trim() : Unknown;

    static string ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            string file = CleanVersion(info.FileVersion);
            return file != Unknown ? file : CleanVersion(info.ProductVersion);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { return Unknown; }
    }

    static string ReadRuntimeVersion(IEnumerable<string> directories, string fileName, CancellationToken token)
    {
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string file = Path.Combine(directory, fileName);
            if (File.Exists(file)) return ReadFileVersion(file);
        }
        return Unknown;
    }

    static string ReadSteamBuild(string gameDirectory, CancellationToken token)
    {
        try
        {
            var child = new DirectoryInfo(gameDirectory);
            for (int depth = 0; depth < 12 && child.Parent != null; depth++, child = child.Parent)
            {
                token.ThrowIfCancellationRequested();
                if (!child.Parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
                    || child.Parent.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) != true) continue;
                foreach (var file in child.Parent.Parent.EnumerateFiles("appmanifest_*.acf", SearchOption.TopDirectoryOnly).Take(1000))
                {
                    token.ThrowIfCancellationRequested();
                    if (file.Length > 1024 * 1024) continue;
                    string build = ParseSteamBuild(File.ReadAllText(file.FullName), child.Name);
                    if (build != Unknown) return build;
                }
                break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Unknown;
    }
}
