using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class MailMonitoringTests
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
        using var app = new AccountFactory();
        using var client = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<MailLogDbContext>>();
        var path = Path.Combine(Path.GetTempPath(), "mu-mail-events-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Email:Provider"] = "smtp", ["EmailMonitor:SnapshotPath"] = path }).Build();
        var sender = new MonitoredEmailSender(new CaptureMail(), factory, config, TimeProvider.System, NullLogger<MonitoredEmailSender>.Instance);
        var id = Guid.NewGuid().ToString("N");
        await sender.SendAsync("monitor@example.test", "784231", "register", id);
        await using var db = await factory.CreateDbContextAsync();
        Check((await db.MailLogs.AsNoTracking().SingleAsync()).Status == "submitted", "SMTP submission logged separately from delivery");
        Check(!System.Text.Json.JsonSerializer.Serialize(await db.MailLogs.AsNoTracking().SingleAsync()).Contains("784231"), "Mail log excludes verification code");
        var failId = Guid.NewGuid().ToString("N");
        sender = new MonitoredEmailSender(new FailingMail(), factory, config, TimeProvider.System, NullLogger<MonitoredEmailSender>.Instance);
        try { await sender.SendAsync("failure@example.test", "784231", "reset", failId); throw new Exception("Failure swallowed"); }
        catch (ApiException) { }
        var failed = await db.MailLogs.AsNoTracking().SingleAsync(x => x.Id == failId);
        Check(failed.Status == "submission_failed" && failed.Detail == "email_unavailable", "Failed submission persisted without raw provider response");
        var monitor = new MailDeliveryMonitor(factory, config, TimeProvider.System, NullLogger<MailDeliveryMonitor>.Instance);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            async Task Import(string status, long at)
            {
                await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new MailDeliverySnapshot(now,
                    [new(id, "QUEUE1", status, at, "250 accepted")])));
                await monitor.ImportAsync(default);
            }
            await Import("delivered", now);
            Check((await db.MailLogs.AsNoTracking().SingleAsync(x => x.Id == id)).Status == "delivered", "Matching SMTP event updates mail row");
            await Import("deferred", now + 1);
            Check((await db.MailLogs.AsNoTracking().SingleAsync(x => x.Id == id)).Status == "delivered", "Terminal delivery cannot regress");
            await Import("delivered", now);
            Check(await db.MailLogs.CountAsync() == 2, "Replay is idempotent");
            Check(monitor.LastCollectionAt == now, "Collector freshness exposed");
            var backup = path + ".sqlite";
            await DatabaseSetup.BackupAsync(db, backup);
            Check(File.Exists(backup), "Mail database consistent backup created");
            File.Delete(backup);
        }
        finally { File.Delete(path); }
        Console.WriteLine($"Mail monitoring passed: {count} assertions.");
        return count;
    }

    private sealed class FailingMail : IVerificationEmailSender
    {
        public Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default)
            => throw new ApiException(503, "email_unavailable", "private transport response");
    }
}
