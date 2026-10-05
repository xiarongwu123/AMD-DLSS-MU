using System.Net;
using System.Security.Cryptography;
using AmdNrAssistant;

public static class DownloadTests
{
    public static async Task RunLive(string output)
    {
        Directory.CreateDirectory(output);
        using var client = new HttpClient(new WebsiteOnlyHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var release = await Core.GetReleaseAsync(client, default);
        var installer = Path.Combine(output, Core.InstallerName);
        await Core.DownloadAsync(client, release, installer, new ProgressSink(), default, Console.WriteLine);
        Console.WriteLine($"LIVE PASS mode1 {new FileInfo(installer).Length} bytes SHA-256 {Core.Hash(installer)}");
        var optiscaler = Path.Combine(output, "Optiscaler.7z");
        await DownloadSources.DownloadAsync(client, OptiInstaller.Url, null, OptiInstaller.Digest, OptiInstaller.Size,
            optiscaler, new ProgressSink(), default, Console.WriteLine, mirror: OptiInstaller.MirrorUrl);
        Console.WriteLine($"LIVE PASS mode2 {new FileInfo(optiscaler).Length} bytes SHA-256 {Core.Hash(optiscaler)}");
    }
    sealed class WebsiteOnlyHandler : DelegatingHandler
    {
        public WebsiteOnlyHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "amd-dlss-mu.claude-api.cn")
                throw new HttpRequestException("GitHub disabled for independent mirror verification");
            return base.SendAsync(request, token);
        }
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var result = response(request); result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }
    sealed class ProgressSink : IProgress<int> { public void Report(int value) { } }
    public static async Task Run(string root, Action<bool, string> assert)
    {
        var bytes = "verified download"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        const string direct = "https://github.com/example/project/releases/download/v1/app.exe";
        const string api = "https://api.github.com/repos/example/project/releases/assets/1";
        var target = Path.Combine(root, "download.exe");
        using var handler = new Handler(r => r.RequestUri!.Host == "github.com"
            ? new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var client = new HttpClient(handler);
        await DownloadSources.DownloadAsync(client, direct, api, hash, bytes.Length, target, new ProgressSink(), default);
        assert(handler.Calls == 3 && File.ReadAllBytes(target).SequenceEqual(bytes), "transient HTTP failure retries once then switches to API and verifies bytes");
        using var badPrimary = new Handler(r => new(HttpStatusCode.OK) {
            Content = new ByteArrayContent(r.RequestUri!.Host == "github.com" ? new byte[bytes.Length] : bytes) });
        using var fallbackClient = new HttpClient(badPrimary);
        await DownloadSources.DownloadAsync(fallbackClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default);
        assert(badPrimary.Calls == 2 && File.ReadAllBytes(target).SequenceEqual(bytes), "wrong primary hash switches source without installing bad bytes");
        using var fail = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes.Length]) });
        using var failClient = new HttpClient(fail);
        bool rejected = false;
        try { await DownloadSources.DownloadAsync(failClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default); }
        catch (IOException) { rejected = true; }
        assert(rejected && File.ReadAllBytes(target).SequenceEqual(bytes), "all bad sources preserve existing destination");
        assert(!Directory.EnumerateFiles(root, "*.partial").Any(), "failed download partials cleaned");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var calls = fail.Calls;
        bool cancelled = false;
        try { await DownloadSources.DownloadAsync(failClient, direct, api, hash, bytes.Length, target, new ProgressSink(), cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        assert(cancelled && fail.Calls == calls, "user cancellation never starts another source");
        bool invalid = false;
        try { DownloadSources.Build("http://github.com/file", api, hash); }
        catch (IOException) { invalid = true; }
        assert(invalid, "reject insecure download address");
        const string mirror = MagpieIntegration.MirrorUrl;
        assert(DownloadSources.Build(direct, api, hash, mirror).SequenceEqual(new[] { mirror, direct, api }), "mirror is tried before both upstream entries");
        using var mirrorOnly = new Handler(r => r.RequestUri!.AbsoluteUri == mirror
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new(HttpStatusCode.ServiceUnavailable));
        using var mirrorClient = new HttpClient(mirrorOnly);
        await DownloadSources.DownloadAsync(mirrorClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default, mirror: mirror);
        assert(mirrorOnly.Calls == 1 && File.ReadAllBytes(target).SequenceEqual(bytes), "healthy mirror avoids GitHub entirely");
        foreach (var badHash in new[] { false, true })
        {
            var requested = new List<string>();
            using var mirrorFailure = new Handler(r => {
                requested.Add(r.RequestUri!.AbsoluteUri);
                return r.RequestUri.AbsoluteUri == api
                    ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : badHash ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes.Length]) }
                    : new(HttpStatusCode.ServiceUnavailable);
            });
            using var mirrorFallback = new HttpClient(mirrorFailure);
            await DownloadSources.DownloadAsync(mirrorFallback, direct, api, hash, bytes.Length, target, new ProgressSink(), default, mirror: mirror);
            var expected = badHash ? new[] { mirror, direct, api } : new[] { mirror, mirror, direct, direct, api };
            assert(requested.SequenceEqual(expected) && File.ReadAllBytes(target).SequenceEqual(bytes),
                "mirror HTTP/hash failures retain both upstream fallbacks and verify final bytes");
        }
        bool badMirror = false;
        try { DownloadSources.Build(direct, api, hash, "http://mirror.example/file"); } catch (IOException) { badMirror = true; }
        assert(badMirror, "mirror must use HTTPS");
        using var metadataDown = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var metadataClient = new HttpClient(metadataDown);
        var release = await Core.GetReleaseAsync(metadataClient, default);
        assert(release.Tag == Core.ReviewedTag && release.Sha256 == Core.ReviewedInstallerSha256
            && release.ApiUrl == Core.InstallerApi && metadataDown.Calls == 0, "pinned mode1 retains both GitHub entries without a metadata dependency");
        using var installerMirror = new Handler(r => r.RequestUri!.AbsoluteUri == Core.InstallerMirror
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : throw new HttpRequestException("GitHub unavailable"));
        using var installerClient = new HttpClient(installerMirror);
        await Core.DownloadAsync(installerClient, release with { Size = bytes.Length, Sha256 = hash }, target, new ProgressSink(), default);
        assert(installerMirror.Calls == 1 && File.ReadAllBytes(target).SequenceEqual(bytes), "mode1 download succeeds from website with GitHub unavailable");
        var timeoutCalls = 0;
        using var timeoutOnce = new Handler(_ => timeoutCalls++ == 0
            ? throw new TaskCanceledException("simulated connection timeout")
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var timeoutClient = new HttpClient(timeoutOnce);
        await DownloadSources.DownloadAsync(timeoutClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default);
        assert(timeoutOnce.Calls == 2 && File.ReadAllBytes(target).SequenceEqual(bytes), "connection timeout retries the same entry before fallback");
        using var missing = new Handler(r => r.RequestUri!.Host == "github.com"
            ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var missingClient = new HttpClient(missing);
        await DownloadSources.DownloadAsync(missingClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default);
        assert(missing.Calls == 2, "permanent HTTP failure switches immediately without retry");
        using var cancelledRetry = new CancellationTokenSource();
        using var repeatedTimeout = new Handler(_ => throw new TaskCanceledException("simulated timeout"));
        using var repeatedClient = new HttpClient(repeatedTimeout);
        bool stopped = false;
        try { await DownloadSources.DownloadAsync(repeatedClient, direct, api, hash, bytes.Length, target,
            new ProgressSink(), cancelledRetry.Token, _ => cancelledRetry.Cancel()); }
        catch (OperationCanceledException) { stopped = true; }
        assert(stopped && repeatedTimeout.Calls == 1, "cancellation during retry prevents every remaining source");
        using var allTimeout = new Handler(_ => throw new TaskCanceledException("simulated timeout"));
        using var allTimeoutClient = new HttpClient(allTimeout);
        bool detailed = false;
        try { await DownloadSources.DownloadAsync(allTimeoutClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default); }
        catch (IOException e) { detailed = e.InnerException is AggregateException errors && errors.InnerExceptions.Count == 4
            && errors.InnerExceptions.All(error => error.Message.Contains("连接超时") && error.Message.Contains("45")); }
        assert(detailed && File.ReadAllBytes(target).SequenceEqual(bytes), "exhausted timeout diagnostics identify host and phase while preserving destination");
        assert(!Directory.EnumerateFiles(root, "*.partial").Any(), "retries and cancellation clean all partial files");
        using var session = new DownloadSession();
        var snapshots = new List<DownloadSnapshot>();
        session.Changed = snapshots.Add;
        session.Report("下载中", 20, bytes.Length, "source");
        session.TogglePause();
        var wait = session.WaitAsync(default);
        assert(session.Paused && !wait.IsCompleted, "pause gate prevents subsequent reads");
        session.TogglePause();
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        assert(!session.Paused, "resume releases read gate");
        session.TogglePause();
        using var stop = new CancellationTokenSource();
        var pausedWait = session.WaitAsync(stop.Token);
        stop.Cancel();
        bool pausedCancelled = false;
        try { await pausedWait; } catch (OperationCanceledException) { pausedCancelled = true; }
        assert(pausedCancelled, "paused download remains cancellable");
        session.TogglePause();
        DownloadSources.CurrentSession.Value = session;
        try
        {
            await DownloadSources.DownloadAsync(client, direct, api, hash, bytes.Length, target, new ProgressSink(), default);
            assert(snapshots.Last().State == "已完成 · 校验通过" && snapshots.Last().Percent == 100, "task completes only after verified download");
            try { await DownloadSources.DownloadAsync(failClient, direct, api, hash, bytes.Length, target, new ProgressSink(), default); } catch (IOException) { }
            assert(snapshots.Last().State == "下载失败", "bad digest produces failed task not completed");
        }
        finally { DownloadSources.CurrentSession.Value = null; }
        var disposable = new DownloadSession();
        DownloadSources.CurrentSession.Value = disposable;
        disposable.Dispose();
        assert(DownloadSources.CurrentSession.Value == null, "disposing task clears ambient download session");
        using var cancelSession = new DownloadSession();
        cancelSession.Report("连接中", 0, bytes.Length, "source"); cancelSession.Cancel();
        DownloadSources.CurrentSession.Value = cancelSession;
        try
        {
            var beforeCalls = handler.Calls; bool taskCancelled = false;
            try { await DownloadSources.DownloadAsync(client, direct, api, hash, bytes.Length, target, new ProgressSink(), default); }
            catch (OperationCanceledException) { taskCancelled = true; }
            assert(taskCancelled && handler.Calls == beforeCalls, "task cancellation reaches downloader without starting request");
        }
        finally { DownloadSources.CurrentSession.Value = null; }
    }
}
