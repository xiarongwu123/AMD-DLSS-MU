using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed record MailDeliveryEvent(string Id, string QueueId, string Status, long At, string Detail);
public sealed record MailDeliverySnapshot(long CollectedAt, MailDeliveryEvent[] Events);

public sealed class MailDeliveryMonitor(IDbContextFactory<MailLogDbContext> factory, IConfiguration config,
    TimeProvider clock, ILogger<MailDeliveryMonitor> logger) : BackgroundService
{
    public long? LastCollectionAt { get; private set; }
    public async Task ImportAsync(CancellationToken ct)
    {
        var path = config["EmailMonitor:SnapshotPath"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("Mail snapshot exceeds size limit.");
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<MailDeliverySnapshot>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (snapshot is null || snapshot.Events is null || snapshot.Events.Length > 10000 || snapshot.CollectedAt > now + 60) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        var pending = (await db.MailLogs.AsNoTracking().Where(x => x.Provider == "smtp" && x.Status != "delivered" && x.Status != "bounced")
            .Select(x => x.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var item in snapshot.Events)
        {
            if (item.Id is null || !pending.Contains(item.Id) || !Guid.TryParseExact(item.Id, "N", out _) || item.QueueId is null || item.QueueId.Length > 32
                || item.Status is not ("delivered" or "deferred" or "bounced") || item.At > now + 60 || item.At <= 0) continue;
            var detail = item.Detail ?? "";
            if (detail.Length > 500) detail = detail[..500];
            await db.MailLogs.Where(x => x.Id == item.Id && x.Provider == "smtp" && x.CreatedAt <= item.At
                && (x.DeliveryEventAt == null || x.DeliveryEventAt < item.At
                    || (x.DeliveryEventAt == item.At && x.Status == "deferred" && item.Status != "deferred"))
                && x.Status != "delivered" && x.Status != "bounced")
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, item.Status)
                    .SetProperty(x => x.DeliveryEventAt, item.At).SetProperty(x => x.UpdatedAt, item.At)
                    .SetProperty(x => x.QueueId, item.QueueId).SetProperty(x => x.Detail, detail), ct);
        }
        LastCollectionAt = snapshot.CollectedAt;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await ImportAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Mail delivery import failed ({ErrorType})", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
