using System.Diagnostics;

namespace AmdNrAssistant;

public static class HipRuntime
{
    public const string DownloadPage = "https://www.amd.com/en/developer/resources/rocm-hub/eula/licenses.html?filename=AMD-Software-PRO-Edition-26.Q3-Win11-For-HIP.exe";
    public const string InstallerName = "AMD-Software-PRO-Edition-26.Q3-Win11-For-HIP.exe";

    public static bool HasVersion72()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.Combine(programFiles, "AMD", "ROCm", "7.2"));
        foreach (var variable in new[] { "HIP_PATH", "HIP_PATH_64", "ROCM_PATH" })
        {
            var path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path)) roots.Add(path);
        }
        foreach (var root in roots)
        {
            try
            {
                var dll = Path.Combine(root, "bin", "amdhip64_7.dll");
                if (!File.Exists(dll)) dll = Path.Combine(root, "amdhip64_7.dll");
                if (!File.Exists(dll)) continue;
                if (Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)).Equals("7.2", StringComparison.OrdinalIgnoreCase) ||
                    IsVersion72(FileVersionInfo.GetVersionInfo(dll).ProductVersion) ||
                    IsVersion72(FileVersionInfo.GetVersionInfo(dll).FileVersion))
                    return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        var systemDll = Path.Combine(Environment.SystemDirectory, "amdhip64_7.dll");
        try
        {
            if (File.Exists(systemDll) &&
                (IsVersion72(FileVersionInfo.GetVersionInfo(systemDll).ProductVersion) ||
                 IsVersion72(FileVersionInfo.GetVersionInfo(systemDll).FileVersion))) return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return false;
    }

    public static bool IsVersion72(string? version) =>
        version != null && System.Text.RegularExpressions.Regex.IsMatch(version, @"(?<!\d)7\.2(?:\.\d+)?(?!\d)");

    public static async Task VerifyAmdInstallerAsync(string path)
    {
        if (!Path.GetFileName(path).Equals(InstallerName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择 AMD 官方 HIP 7.2 安装包：" + InstallerName);
        if (!File.Exists(path)) throw new FileNotFoundException("AMD HIP 7.2 安装包未找到。", path);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new IOException("无法找到 Windows 签名验证工具，已停止运行 AMD 安装包。");
        using var process = new Process { StartInfo = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("$s = Get-AuthenticodeSignature -LiteralPath $env:MU_HIP_INSTALLER; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'Advanced Micro Devices') { exit 0 } else { exit 1 }");
        process.StartInfo.Environment["MU_HIP_INSTALLER"] = Path.GetFullPath(path);
        if (!process.Start()) throw new IOException("无法验证 AMD 安装包签名。");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("AMD 安装包数字签名无效或发布者不是 AMD，已停止运行。");
    }

    public static ProcessStartInfo CreateInstallStartInfo(string path) => new(path)
    {
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!
    };
}
