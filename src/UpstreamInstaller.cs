using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public sealed class MissingAmdHipRuntimeException : IOException
{
    public MissingAmdHipRuntimeException() : base("缺少 AMD HIP 运行时 amdhip64_7.dll。当前安装方案面向 AMD 显卡，不能仅使用 NVIDIA 显卡完成此方案。AMD 用户请检查受支持的显卡及 Adrenalin 驱动；已停止安装，不会忽略依赖警告。") { }
}

// Only the pinned upstream protocol is accepted. Never send a blanket sequence of Y's.
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
        var protocol = new InstallerProtocol(gameExe);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var process = new Process { StartInfo = new ProcessStartInfo(installer)
        {
            WorkingDirectory = Path.GetDirectoryName(gameExe)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }};
        Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        try
        {
            if (!process.Start()) throw new IOException("无法启动上游安装器。");
            progress.Report("正在后台运行上游安装器…");
            var lastOutput = DateTime.UtcNow;
            async Task ReadOutput(StreamReader reader, bool interactive)
            {
                var buffer = new char[2048];
                var errorOutput = new StringBuilder();
                while (true)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token);
                    if (count == 0) return;
                    lastOutput = DateTime.UtcNow;
                    var chunk = new string(buffer, 0, count);
                    log(chunk);
                    if (!interactive)
                    {
                        errorOutput.Append(chunk);
                        if (errorOutput.Length > 65536) throw new IOException("安装器错误输出超出预期，已停止。");
                        InstallerProtocol.CheckWarnings(errorOutput.ToString());
                        continue;
                    }
                    var reply = protocol.Feed(chunk);
                    if (reply == null) continue;
                    await process.StandardInput.WriteLineAsync(reply.AsMemory(), timeout.Token);
                    await process.StandardInput.FlushAsync(timeout.Token);
                    progress.Report(reply == "y" ? "游戏目录已核对，正在检查依赖…" : "游戏 EXE 已匹配，正在安装组件…");
                }
            }
            stdout = ReadOutput(process.StandardOutput, true);
            stderr = ReadOutput(process.StandardError, false);
            var exited = process.WaitForExitAsync(timeout.Token);
            while (!exited.IsCompleted)
            {
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                if (DateTime.UtcNow - lastOutput > TimeSpan.FromSeconds(90))
                    throw new IOException("安装器长时间没有响应，可能等待未知提示或不支持后台输入。已停止自动安装；请查看诊断，不会自动确认未知选项。");
                await Task.WhenAny(exited, Task.Delay(250, timeout.Token));
                timeout.Token.ThrowIfCancellationRequested();
            }
            await exited;
            await Task.WhenAll(stdout, stderr);
            protocol.EnsureCompleted();
            if (process.ExitCode != 0) throw new IOException("上游安装器失败，退出码：" + process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("上游安装器超时，已停止；请查看诊断并检查游戏目录。");
        }
        finally
        {
            timeout.Cancel();
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr); } catch { /* Preserve the original failure after observing reader tasks. */ }
        }
    }
}
