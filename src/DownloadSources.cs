using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AmdNrAssistant;

public static class DownloadSources
{
    public static readonly AsyncLocal<DownloadSession?> CurrentSession = new();
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan ReadIdleTimeout = TimeSpan.FromSeconds(90);
    const int AttemptsPerSource = 3;
    public static string? AssetApi(string repository, JsonElement asset) =>
        asset.TryGetProperty("id", out var id) && id.TryGetInt64(out var number) && number > 0
            ? "https://api.github.com/repos/" + repository + "/releases/assets/" + number : null;

    public static string[] Build(string primary, string? api, string hash, string? mirror = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new IOException("下载校验值无效。");
        var sources = (mirror == null ? Array.Empty<string>() : new[] { mirror }).Concat(new[] { primary })
            .Concat(api == null ? Array.Empty<string>() : new[] { api }).Distinct().ToArray();
        foreach (var source in sources)
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new IOException("下载地址必须是 HTTPS。");
        return sources;
    }

    public static async Task DownloadAsync(HttpClient client, string primary, string? api, string hash, long size,
        string destination, IProgress<int> progress, CancellationToken token, Action<string>? status = null, string? mirror = null)
    {
        var session = CurrentSession.Value;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, session?.Token ?? CancellationToken.None);
        token = linked.Token;
        session?.Report("连接中", 0, size, "正在连接下载源");
        try
        {
            Core.RejectLinks(destination);
            if (size <= 0) throw new IOException("下载大小无效。");
            var sources = Build(primary, api, hash, mirror);
            // A pinned digest binds persisted bytes to this exact package, including across sources.
            var partial = destination + "." + hash.ToLowerInvariant() + ".partial";
            var lockPath = partial + ".lock";
            Core.RejectLinks(partial);
            Core.RejectLinks(lockPath);
            token.ThrowIfCancellationRequested();
            using var downloadLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(partial) && new FileInfo(partial).Length > size) File.Delete(partial);
            if (File.Exists(partial) && new FileInfo(partial).Length == size)
            {
                session?.Report("正在校验", 100, size, "校验上次保留的完整缓存");
                if (string.Equals(await Task.Run(() => Core.Hash(partial), token).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    File.Move(partial, destination, true);
                    progress.Report(100);
                    session?.Report("已完成 · 校验通过", 100, size, "缓存 SHA-256 校验通过");
                    return;
                }
                File.Delete(partial);
            }
            var failures = new List<Exception>();
            for (int index = 0; index < sources.Length; index++)
            {
                var url = new Uri(sources[index]);
                for (int attempt = 0; attempt < AttemptsPerSource; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    var phase = "读取缓存";
                    long total = 0;
                    var retry = false;
                    try
                    {
                        total = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                        if (total < size)
                        {
                            phase = "连接";
                            var detail = $"下载源 {index + 1}/{sources.Length}：{url.Host} · 尝试 {attempt + 1}/{AttemptsPerSource}";
                            if (total > 0) detail += $" · 从 {total / 1048576d:0.00} MiB 续传";
                            status?.Invoke(detail);
                            progress.Report(Percent(total, size));
                            session?.Report("连接中", Percent(total, size), size, detail);
                            using var overall = CancellationTokenSource.CreateLinkedTokenSource(token);
                            overall.CancelAfter(TimeSpan.FromMinutes(30));
                            using var headers = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                            headers.CancelAfter(ConnectionTimeout);
                            using var request = new HttpRequestMessage(HttpMethod.Get, url);
                            request.Headers.UserAgent.ParseAdd("AMD-DLSS-MU");
                            request.Headers.Accept.ParseAdd("application/octet-stream");
                            request.Headers.AcceptEncoding.ParseAdd("identity");
                            if (total > 0) request.Headers.Range = new RangeHeaderValue(total, null);
                            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
                            response.EnsureSuccessStatusCode();
                            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("下载被重定向到非 HTTPS 地址。");
                            if (response.StatusCode == HttpStatusCode.PartialContent)
                            {
                                var range = response.Content.Headers.ContentRange;
                                if (range?.Unit != "bytes" || range.From != total || range.Length != size ||
                                    range.To == null || range.To < total || range.To >= size ||
                                    response.Content.Headers.ContentLength is long length && length != range.To - total + 1)
                                    throw new IOException("服务器返回的 Content-Range 与续传位置或文件大小不一致。");
                            }
                            else if (response.StatusCode == HttpStatusCode.OK)
                            {
                                // A server may ignore Range; replace the prefix instead of appending a full response.
                                total = 0;
                                if (response.Content.Headers.ContentLength is long length && length != size)
                                    throw new IOException($"服务器文件大小不符：预期 {size} 字节，实际 {length} 字节。");
                            }
                            else throw new IOException($"服务器返回了非预期状态 HTTP {(int)response.StatusCode}。");
                            phase = "读取";
                            await using var input = await response.Content.ReadAsStreamAsync(overall.Token).ConfigureAwait(false);
                            phase = "写入缓存";
                            await using (var output = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                output.SetLength(total);
                                output.Position = total;
                                progress.Report(Percent(total, size));
                                session?.Report("下载中", Percent(total, size), size, detail);
                                var buffer = new byte[81920];
                                while (true)
                                {
                                    phase = "等待继续";
                                    if (session != null) await session.WaitAsync(token).ConfigureAwait(false);
                                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                                    idle.CancelAfter(ReadIdleTimeout);
                                    phase = "读取";
                                    var count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                                    if (count == 0) break;
                                    var end = response.StatusCode == HttpStatusCode.PartialContent
                                        ? response.Content.Headers.ContentRange!.To!.Value + 1 : size;
                                    if (count > end - total) { phase = "校验"; throw new IOException("下载超过响应声明的大小。"); }
                                    phase = "写入缓存";
                                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                                    total += count;
                                    progress.Report(Percent(total, size));
                                    session?.Report("下载中", Percent(total, size), size, url.Host);
                                }
                                phase = "写入缓存";
                                await output.FlushAsync(token).ConfigureAwait(false);
                                phase = "读取";
                                if (total != size) throw new IOException($"连接提前结束：已下载 {total}/{size} 字节。");
                            }
                        }
                        phase = "校验";
                        session?.Report("正在校验", 100, size, "验证完整文件 SHA-256");
                        if (!string.Equals(await Task.Run(() => Core.Hash(partial), token).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("SHA-256 不匹配，已丢弃缓存并切换下载源。");
                        phase = "保存文件";
                        token.ThrowIfCancellationRequested();
                        File.Move(partial, destination, true);
                        session?.Report("已完成 · 校验通过", 100, size, "SHA-256 校验通过");
                        return;
                    }
                    catch (Exception e) when (!token.IsCancellationRequested && e is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
                    {
                        var reason = e is OperationCanceledException
                            ? $"操作超时（连接上限 {ConnectionTimeout.TotalSeconds:0} 秒，读取停滞上限 {ReadIdleTimeout.TotalSeconds:0} 秒，单次请求上限 30 分钟）"
                            : ErrorReason(e);
                        var message = $"{url.Host} · 尝试 {attempt + 1}/{AttemptsPerSource} · {phase}失败 · {total}/{size} 字节：{reason}";
                        failures.Add(new IOException(message, e));
                        if (phase == "校验" || e is HttpRequestException { StatusCode: HttpStatusCode.RequestedRangeNotSatisfiable })
                            File.Delete(partial);
                        if (phase is "写入缓存" or "保存文件" or "读取缓存" || e is UnauthorizedAccessException)
                            throw new IOException(message + $"\n缓存位置：{partial}\n请检查磁盘空间、目录写入权限及安全软件拦截。", e);
                        retry = attempt + 1 < AttemptsPerSource && (e is OperationCanceledException
                            || e is HttpRequestException http && (http.StatusCode == null || http.StatusCode is HttpStatusCode.RequestTimeout
                                or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestedRangeNotSatisfiable || (int?)http.StatusCode >= 500)
                            || e is IOException && phase == "读取");
                        var next = retry ? "正在重试当前下载源，保留已下载部分。" : index + 1 < sources.Length
                            ? "正在切换备用入口。" : "下载已停止，保留可续传部分。";
                        status?.Invoke(message + "\n" + next);
                        session?.Report("连接中", Percent(total, size), size, message + "\n" + next);
                    }
                    if (!retry) break;
                    await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token).ConfigureAwait(false);
                }
            }
            throw new IOException("下载失败，已尝试所有入口；点击重试可继续下载。\n" +
                string.Join("\n", failures.Select(e => e.Message)) + $"\n缓存位置：{partial}", new AggregateException(failures));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session?.Report("已取消", session.Percent, size, "下载已停止，已保留缓存；重试时续传，完整校验通过后才使用文件。");
            throw;
        }
        catch (Exception e)
        {
            session?.Report("下载失败", session.Percent, size, e.Message);
            throw;
        }
    }

    static int Percent(long downloaded, long size) => (int)Math.Clamp(downloaded * 100d / size, 0, 100);

    static string ErrorReason(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            var message = current is HttpRequestException { StatusCode: not null } http
                ? $"HTTP {(int)http.StatusCode.Value}：{current.Message}" : current.Message;
            if (!messages.Contains(message)) messages.Add(message);
        }
        return string.Join(" → ", messages);
    }
}

public sealed record DownloadSnapshot(string State, int Percent, long Size, string Detail);

public sealed class DownloadSession : IDisposable
{
    readonly CancellationTokenSource cancel = new();
    readonly object gate = new();
    TaskCompletionSource? resume;
    DownloadSnapshot current = new("连接中", 0, 0, "");
    bool disposed;
    public Action<DownloadSnapshot>? Changed { get; set; }
    public event Action? Disposed;
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
    public void Cancel() { lock (gate) { if (!disposed && current.State is ("连接中" or "下载中" or "已暂停")) cancel.Cancel(); } }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; resume?.TrySetResult(); resume = null; }
        if (ReferenceEquals(DownloadSources.CurrentSession.Value, this)) DownloadSources.CurrentSession.Value = null;
        cancel.Dispose();
        Disposed?.Invoke();
    }
}
