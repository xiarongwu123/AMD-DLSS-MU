using System.Net;
using System.Security.Cryptography;
using AmdNrAssistant;

public static class DownloadTests
{
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
        assert(handler.Calls == 2 && File.ReadAllBytes(target).SequenceEqual(bytes), "HTTP failure switches to API and verifies bytes");
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
        using var metadataDown = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var metadataClient = new HttpClient(metadataDown);
        var release = await Core.GetReleaseAsync(metadataClient, default);
        assert(release.Tag == Core.ReviewedTag && release.Sha256 == Core.ReviewedInstallerSha256, "metadata outage falls back to pinned mode1 release");
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
