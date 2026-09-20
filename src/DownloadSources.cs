using System.Net.Http;
using System.Text.Json;

namespace AmdNrAssistant;

public static class DownloadSources
{
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
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(45));
                        var count = await input.ReadAsync(buffer, idle.Token);
                        if (count == 0) break;
                        total += count;
                        if (total > size) throw new IOException("下载超过预期大小。");
                        await output.WriteAsync(buffer.AsMemory(0, count), overall.Token);
                        progress.Report((int)(total * 100 / size));
                    }
                    if (total != size) throw new IOException("下载不完整。");
                }
                if (!string.Equals(Core.Hash(partial), hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SHA-256 不匹配。");
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination, true);
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
}
