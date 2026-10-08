using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AmdNrAssistant;

public static class DownloadResumeTests
{
    sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<(string Host, long? Offset)> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add((request.RequestUri!.Host, request.Headers.Range?.Ranges.Single().From));
            var response = respond(request, Requests.Count);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    sealed class ProgressSink(Action<int>? report = null) : IProgress<int>
    {
        public void Report(int value) => report?.Invoke(value);
    }

    sealed class InterruptedStream(byte[] prefix) : MemoryStream(prefix)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Position == Length) throw new IOException("simulated connection reset", new IOException("socket closed"));
            return base.ReadAsync(buffer, token);
        }
    }

    sealed class LiveHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public readonly List<(long? Offset, HttpStatusCode Status)> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsoluteUri != MagpieIntegration.MirrorUrl)
                throw new HttpRequestException("Live verification requires the MU mirror; upstream fallbacks disabled");
            var response = await base.SendAsync(request, token);
            Requests.Add((request.Headers.Range?.Ranges.Single().From, response.StatusCode));
            return response;
        }
    }

    public static async Task RunLive(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var stop = new CancellationTokenSource();
        Task Download(CancellationToken token, IProgress<int> progress) => DownloadSources.DownloadAsync(client,
            MagpieIntegration.Repository + "/releases/download/" + MagpieIntegration.Tag + "/Magpie-Experimental-x64.zip",
            null, MagpieIntegration.Sha256, MagpieIntegration.PackageSize, destination, progress, token,
            Console.WriteLine, MagpieIntegration.MirrorUrl);
        bool cancelled = false;
        try { await Download(stop.Token, new ProgressSink(value => { if (value >= 2) stop.Cancel(); })); }
        catch (OperationCanceledException) { cancelled = true; }
        var partial = destination + "." + MagpieIntegration.Sha256 + ".partial";
        if (!cancelled || !File.Exists(partial)) throw new Exception("Live interruption did not preserve partial");
        var retained = new FileInfo(partial).Length;
        Console.WriteLine($"LIVE interrupted at {retained} bytes; starting a new download invocation");
        int nextProgress = 10;
        var firstResumeRequest = handler.Requests.Count;
        await Download(default, new ProgressSink(value =>
        {
            if (value < nextProgress) return;
            Console.WriteLine($"LIVE progress {value}%");
            nextProgress = value / 10 * 10 + 10;
        }));
        if (handler.Requests[firstResumeRequest] != (retained, HttpStatusCode.PartialContent))
            throw new Exception("Live resume did not receive HTTP 206 at the retained offset");
        if (new FileInfo(destination).Length != MagpieIntegration.PackageSize || Core.Hash(destination) != MagpieIntegration.Sha256)
            throw new Exception("Live resumed package hash mismatch");
        Console.WriteLine($"LIVE PASS Magpie Range bytes={retained}- HTTP 206; {MagpieIntegration.PackageSize} bytes SHA-256 {MagpieIntegration.Sha256}");
    }

    public static async Task Run(string root, Action<bool, string> assert)
    {
        var bytes = "verified resumable package"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        const string direct = "https://download.example/package.zip";
        const string api = "https://fallback.example/package.zip";
        var folder = Path.Combine(root, "resume");
        Directory.CreateDirectory(folder);
        string Target(string name) => Path.Combine(folder, name + ".zip");
        string Partial(string target) => target + "." + hash + ".partial";
        Task Download(HttpClient client, string target, CancellationToken token = default, IProgress<int>? progress = null) =>
            DownloadSources.DownloadAsync(client, direct, api, hash, bytes.Length, target, progress ?? new ProgressSink(), token);
        HttpResponseMessage Full() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        HttpResponseMessage Remainder(long start)
        {
            var result = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[(int)start..]) };
            result.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, bytes.Length - 1, bytes.Length);
            return result;
        }
        HttpResponseMessage Interrupted()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(bytes[..5])) };
            response.Content.Headers.ContentLength = bytes.Length;
            return response;
        }

        assert(DownloadSources.ConnectionTimeout == TimeSpan.FromSeconds(45) && DownloadSources.ReadIdleTimeout == TimeSpan.FromSeconds(90),
            "restored connection and idle timeout limits");
        using (var handler = new Handler((_, call) => call == 1 ? throw new TaskCanceledException("simulated timeout") : Full()))
        using (var client = new HttpClient(handler))
        {
            await Download(client, Target("timeout"));
            assert(handler.Requests.Count == 2 && handler.Requests.All(r => r.Host == "download.example"), "timeout retries current source before fallback");
        }
        using (var handler = new Handler((request, call) => call == 1 ? Interrupted() : Remainder(request.Headers.Range!.Ranges.Single().From!.Value)))
        using (var client = new HttpClient(handler))
        {
            var target = Target("interrupted");
            await Download(client, target);
            assert(handler.Requests.Select(r => r.Offset).SequenceEqual(new long?[] { null, 5 }) && File.ReadAllBytes(target).SequenceEqual(bytes),
                "read reset resumes from persisted byte offset on same source");
            assert(!File.Exists(Partial(target)), "verified resumed file is promoted and partial disappears");
        }
        var persistedTarget = Target("persisted");
        using (var handler = new Handler((_, call) => call == 1 ? Interrupted() : throw new HttpRequestException("offline")))
        using (var client = new HttpClient(handler))
        {
            using var session = new DownloadSession();
            var snapshots = new List<DownloadSnapshot>();
            session.Changed = snapshots.Add;
            DownloadSources.CurrentSession.Value = session;
            try
            {
                await Download(client, persistedTarget);
                throw new Exception("Expected exhausted download");
            }
            catch (IOException e)
            {
                assert(handler.Requests.Count == 6 && new FileInfo(Partial(persistedTarget)).Length == 5,
                    "exhausted attempts retain prefix for next invocation and source fallback");
                assert(e.Message.Contains("读取失败") && e.Message.Contains("socket closed") && e.Message.Contains("fallback.example")
                    && e.Message.Contains($"5/{bytes.Length}") && e.InnerException is AggregateException,
                    "final diagnostic contains host, attempt, phase, byte count and nested network reason");
                assert(snapshots.Last().State == "下载失败" && snapshots.Last().Detail == e.Message,
                    "download details retain actual errors instead of generic message");
            }
            finally { DownloadSources.CurrentSession.Value = null; }
        }
        using (var handler = new Handler((request, _) => Remainder(request.Headers.Range!.Ranges.Single().From!.Value)))
        using (var client = new HttpClient(handler))
        {
            await Download(client, persistedTarget);
            assert(handler.Requests.Single().Offset == 5 && File.ReadAllBytes(persistedTarget).SequenceEqual(bytes), "new invocation continues old partial");
        }
        foreach (var scenario in new[] { "ignored-range", "bad-range", "range-416", "bad-hash", "complete-cache", "oversized-cache", "corrupt-complete" })
        {
            var target = Target(scenario);
            var cached = scenario switch
            {
                "complete-cache" => bytes,
                "oversized-cache" => new byte[bytes.Length + 1],
                "corrupt-complete" => new byte[bytes.Length],
                _ => bytes[..5]
            };
            File.WriteAllBytes(Partial(target), cached);
            using var handler = new Handler((request, call) =>
            {
                if (scenario == "bad-range" && call == 1) return Remainder(4);
                if (scenario == "range-416" && call == 1) return new(HttpStatusCode.RequestedRangeNotSatisfiable);
                if (scenario == "bad-hash" && call == 1)
                {
                    var bad = Remainder(5);
                    bad.Content = new ByteArrayContent(new byte[bytes.Length - 5]);
                    bad.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, bytes.Length - 1, bytes.Length);
                    return bad;
                }
                return scenario == "bad-range" ? Remainder(request.Headers.Range!.Ranges.Single().From!.Value) : Full();
            });
            using var client = new HttpClient(handler);
            await Download(client, target);
            assert(File.ReadAllBytes(target).SequenceEqual(bytes), scenario + " never promotes incorrect bytes");
            if (scenario == "ignored-range") assert(handler.Requests.Count == 1 && handler.Requests[0].Offset == 5, "ignored Range safely restarts full response");
            if (scenario == "bad-range") assert(handler.Requests.Count == 2 && handler.Requests[1].Host == "fallback.example" && handler.Requests[1].Offset == 5,
                "invalid Content-Range never appends and fallback retains valid prefix");
            if (scenario == "range-416") assert(handler.Requests.Count == 2 && handler.Requests[1].Offset == null && handler.Requests[1].Host == "download.example",
                "416 discards stale prefix and retries same source from zero");
            if (scenario == "bad-hash") assert(handler.Requests.Count == 2 && handler.Requests[1].Offset == null, "digest mismatch discards entire corrupt prefix before fallback");
            if (scenario == "complete-cache") assert(handler.Requests.Count == 0, "complete valid persisted cache is verified without network");
            if (scenario is "oversized-cache" or "corrupt-complete") assert(handler.Requests.Single().Offset == null, "invalid persisted cache restarts from zero");
        }
        using (var stop = new CancellationTokenSource())
        using (var handler = new Handler((_, _) => Interrupted()))
        using (var client = new HttpClient(handler))
        {
            var target = Target("cancelled");
            bool cancelled = false;
            try { await Download(client, target, stop.Token, new ProgressSink(value => { if (value > 0) stop.Cancel(); })); }
            catch (OperationCanceledException) { cancelled = true; }
            assert(cancelled && handler.Requests.Count == 1 && new FileInfo(Partial(target)).Length == 5 && !File.Exists(target),
                "user cancellation stops retries immediately and preserves unverified prefix");
            using var resumeHandler = new Handler((request, _) => Remainder(request.Headers.Range!.Ranges.Single().From!.Value));
            using var resumeClient = new HttpClient(resumeHandler);
            await Download(resumeClient, target);
            assert(resumeHandler.Requests.Single().Offset == 5, "cancelled download resumes after retry");
        }
        using (var handler = new Handler((_, _) => new(HttpStatusCode.NotFound)))
        using (var client = new HttpClient(handler))
        {
            try { await Download(client, Target("404")); throw new Exception("Expected HTTP error"); }
            catch (IOException e) { assert(handler.Requests.Count == 2 && e.Message.Contains("HTTP 404"), "permanent HTTP errors skip retries and surface status code"); }
        }
        var blockedTarget = Target("write-error");
        Directory.CreateDirectory(Partial(blockedTarget));
        using (var handler = new Handler((_, _) => Full()))
        using (var client = new HttpClient(handler))
        {
            try { await Download(client, blockedTarget); throw new Exception("Expected cache write error"); }
            catch (IOException e) { assert(handler.Requests.Count == 1 && e.Message.Contains("写入缓存") && e.Message.Contains("磁盘空间"),
                "local write failures identify storage cause without blaming all network sources"); }
        }
    }
}
