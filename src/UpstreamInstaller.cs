using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public sealed class MissingAmdHipRuntimeException : IOException
{
    public MissingAmdHipRuntimeException() : base("缺少 AMD HIP 运行时 amdhip64_7.dll。当前安装方案面向 AMD 显卡，不能仅使用 NVIDIA 显卡完成此方案。AMD 用户请检查受支持的显卡及 Adrenalin 驱动；已停止安装，不会忽略依赖警告。") { }
}

// Legacy v0.3.1 protocol retained for regression coverage only.
// The v0.4.0 runner below never uses this stdin protocol.
public sealed class InstallerProtocol(string gameExe)
{
    readonly StringBuilder output = new();
    bool folderConfirmed, executableConfirmed;
    static string Normalize(string value) => value.Trim().Replace('/', '\\').TrimEnd('\\');
    public string? Feed(string chunk)
    {
        output.Append(chunk);
        if (output.Length > 65536) throw new IOException("安装器输出超出预期，已停止自动安装。");
        var text = output.ToString();
        CheckWarnings(text);
        if (!folderConfirmed && text.Contains("Use this folder? [Y/n]", StringComparison.Ordinal))
        {
            var folder = Regex.Match(text, @"Game folder:\s*([^\r\n]+)");
            var expected = gameExe.Replace('/', '\\');
            var parent = expected[..expected.LastIndexOf('\\')];
            if (!folder.Success || !Normalize(folder.Groups[1].Value).Equals(Normalize(parent), StringComparison.OrdinalIgnoreCase))
                throw new IOException("安装器目标目录与所选游戏不一致，已停止。");
            folderConfirmed = true;
            output.Clear();
            return "y";
        }
        if (!executableConfirmed && text.Contains("Which one is the game?", StringComparison.Ordinal) && text.Contains("type another number:", StringComparison.Ordinal))
        {
            if (!folderConfirmed) throw new IOException("安装器提示顺序异常，已停止。");
            var name = gameExe.Replace('/', '\\').Split('\\').Last();
            var entries = Regex.Matches(text, @"(?m)^\s*\[(\d+)\]\s+(.+?\.exe)(?=\s|$)", RegexOptions.IgnoreCase);
            var matches = entries.Cast<Match>().Where(m => m.Groups[2].Value.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new IOException("安装器未唯一列出所选游戏 EXE，已停止；不会选择崩溃处理器或其他程序。");
            executableConfirmed = true;
            output.Clear();
            return matches[0].Groups[1].Value;
        }
        return null;
    }
    public static void CheckWarnings(string text)
    {
        if (Regex.IsMatch(text, @"AMD\s+HIP\s+runtime[^\r\n]*?\b(?:was|is)\s+not\s+found\b", RegexOptions.IgnoreCase))
            throw new MissingAmdHipRuntimeException();
        if (Regex.IsMatch(text, @"Install anyway\?\s*\[y/N\]", RegexOptions.IgnoreCase))
            throw new IOException("上游要求忽略安装警告，已停止。请查看诊断并解决依赖问题后重试。");
    }
    public void EnsureCompleted()
    {
        if (!folderConfirmed || !executableConfirmed) throw new IOException("安装器未完成可识别的目录和 EXE 确认，无法确认自动安装完成。");
    }
}

public static class UpstreamInstaller
{
    public static async Task RunAsync(string installer, string gameExe, IProgress<string> progress,
        Action<string> log, CancellationToken cancellationToken)
    {
        Core.ValidateInstaller(installer);
        if (new FileInfo(installer).Length != Core.ReviewedInstallerSize ||
            !Core.Hash(installer).Equals(Core.ReviewedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装器不属于已核验的上游版本，拒绝自动执行。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        // v0.4.0 is a graphical installer, not the legacy stdin protocol.
        // Do not invent silent flags or feed confirmations into a GUI process.
        using var process = new Process { StartInfo = CreateStartInfo(installer, gameExe) };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("无法启动上游安装器。");
            log($"Started official {Core.ReviewedTag} graphical installer for {gameExe}");
            progress.Report("请在上游 v0.4.0 安装窗口确认游戏 EXE 并完成安装，然后关闭该窗口；客户端将校验结果。");
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new IOException("上游安装器失败，退出码：" + process.ExitCode);
            var check = Core.CheckInstalled(Path.GetDirectoryName(gameExe)!);
            if (!check.Ready) throw new IOException("上游安装窗口已关闭，但配置尚未完成：" + check.Message);
            log("Official graphical installer exited; installed files validated.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("等待上游安装窗口超时，已停止；请查看诊断并检查游戏目录。");
        }
        finally
        {
            timeout.Cancel();
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        }
    }

    public static ProcessStartInfo CreateStartInfo(string installer, string gameExe) => new(installer)
    {
        WorkingDirectory = Path.GetDirectoryName(gameExe)!,
        UseShellExecute = false,
        CreateNoWindow = false,
        // No arguments: upstream does not document a silent installation API.
        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };
}
