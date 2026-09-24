using System.Net.Http;
using System.Text.Json;

namespace AmdNrAssistant;

public static class DownloadSources
{
    public static readonly AsyncLocal<DownloadSession?> CurrentSession = new();
    public static string? AssetApi(string repository, JsonElement asset) =>
        asset.TryGetProperty("id", out var id) && id.TryGetInt64(out var number) && number > 0
            ? "https://api.github.com/repos/" + repository + "/releases/assets/" + number : null;

    public static string[] Build(string primary, string? api, string hash)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new IOException("下载校验值无效。");
        var sources = new[] { primary }.Concat(api == null ? Array.Empty<string>() : new[] { api }).Distinct().ToArray();
        foreach (var source in sources)
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new IOException("下载地址必须是 HTTPS。");
        return sources;
    }

    public static async Task DownloadAsync(HttpClient client, string primary, string? api, string hash, long size,
        string destination, IProgress<int> progress, CancellationToken token, Action<string>? status = null)
    {
        var session = CurrentSession.Value;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, session?.Token ?? CancellationToken.None);
        token = linked.Token;
        session?.Report("连接中", 0, size, "正在连接下载源");
        try
        {
        Core.RejectLinks(destination);
        if (size <= 0) throw new IOException("下载大小无效。");
        var sources = Build(primary, api, hash);
        var failures = new List<Exception>();
        for (int index = 0; index < sources.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var url = new Uri(sources[index]);
            if (url.Scheme != "https") throw new IOException("下载地址必须是 HTTPS。");
            var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                status?.Invoke($"下载源 {index + 1}/{sources.Length}：{url.Host}");
                session?.Report("连接中", 0, size, $"下载源 {index + 1}/{sources.Length}：{url.Host}");
                progress.Report(0);
                using var overall = CancellationTokenSource.CreateLinkedTokenSource(token);
                overall.CancelAfter(TimeSpan.FromMinutes(30));
                using var headers = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                headers.CancelAfter(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("AMD-DLSS-MU");
                request.Headers.Accept.ParseAdd("application/octet-stream");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("下载被重定向到非 HTTPS 地址。");
                await using (var input = await response.Content.ReadAsStreamAsync(overall.Token))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long total = 0;
                    while (true)
                    {
                        if (session != null) await session.WaitAsync(overall.Token);
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(45));
                        var count = await input.ReadAsync(buffer, idle.Token);
                        if (count == 0) break;
                        total += count;
                        if (total > size) throw new IOException("下载超过预期大小。");
                        await output.WriteAsync(buffer.AsMemory(0, count), overall.Token);
                        progress.Report((int)(total * 100 / size));
                        session?.Report("下载中", (int)(total * 100 / size), size, url.Host);
                    }
                    if (total != size) throw new IOException("下载不完整。");
                }
                session?.Report("正在校验", 100, size, "验证 SHA-256");
                if (!string.Equals(Core.Hash(partial), hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SHA-256 不匹配。");
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination, true);
                session?.Report("已完成 · 校验通过", 100, size, "SHA-256 校验通过");
                return;
            }
            catch (Exception e) when (!token.IsCancellationRequested && e is HttpRequestException or IOException or OperationCanceledException)
            {
                failures.Add(e);
                status?.Invoke($"下载源 {url.Host} 失败：{e.Message}" + (index + 1 < sources.Length ? "；正在切换…" : ""));
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        throw new IOException("下载暂时失败，已自动尝试备用入口。请稍后重试。", new AggregateException(failures));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session?.Report("已取消", session.Percent, size, "下载已停止，未使用未校验文件");
            throw;
        }
        catch (Exception e)
        {
            session?.Report("下载失败", session.Percent, size, e.Message);
            throw;
        }
    }
}

public sealed record DownloadSnapshot(string State, int Percent, long Size, string Detail);

public sealed class DownloadSession : IDisposable
{
    readonly CancellationTokenSource cancel = new();
    readonly object gate = new();
    TaskCompletionSource? resume;
    DownloadSnapshot current = new("连接中", 0, 0, "");
    public Action<DownloadSnapshot>? Changed { get; set; }
    public CancellationToken Token => cancel.Token;
    public int Percent { get { lock (gate) return current.Percent; } }
    public bool Paused { get { lock (gate) return resume != null; } }
    public void Report(string state, int percent, long size, string detail)
    {
        DownloadSnapshot snapshot;
        lock (gate)
        {
            snapshot = new(resume != null && state == "下载中" ? "已暂停" : state, percent, size, detail);
            if (snapshot == current) return;
            current = snapshot;
        }
        Changed?.Invoke(snapshot);
    }
    public void TogglePause()
    {
        DownloadSnapshot snapshot;
        lock (gate)
        {
            if (current.State is not ("下载中" or "已暂停")) return;
            if (resume == null) resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            else { resume.TrySetResult(); resume = null; }
            snapshot = current = current with { State = resume == null ? "下载中" : "已暂停" };
        }
        Changed?.Invoke(snapshot);
    }
    public async Task WaitAsync(CancellationToken token)
    {
        Task? task; lock (gate) task = resume?.Task;
        if (task != null) await task.WaitAsync(token);
    }
    public void Cancel() { lock (gate) { if (current.State is "连接中" or "下载中" or "已暂停") cancel.Cancel(); } }
    public void Dispose()
    {
        lock (gate) { resume?.TrySetResult(); resume = null; }
        if (ReferenceEquals(DownloadSources.CurrentSession.Value, this)) DownloadSources.CurrentSession.Value = null;
        cancel.Dispose();
    }
}
