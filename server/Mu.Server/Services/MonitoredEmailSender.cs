using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class MonitoredEmailSender(IVerificationEmailSender inner, IDbContextFactory<MailLogDbContext> factory,
    IConfiguration config, TimeProvider clock, ILogger<MonitoredEmailSender> logger) : IVerificationEmailSender
{
    public async Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.MailLogs.Add(new() { Id = requestId, Email = email, Purpose = purpose,
            Provider = config["Email:Provider"]?.ToLowerInvariant() ?? "resend",
            CreatedAt = clock.GetUtcNow().ToUnixTimeSeconds(), UpdatedAt = clock.GetUtcNow().ToUnixTimeSeconds() });
        // Persist before dispatch; a process crash leaves an explicit uncertain record.
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await inner.SendAsync(email, code, purpose, requestId, cancellationToken);
        }
        catch (Exception error)
        {
            await UpdateAsync("submission_failed", error is ApiException api ? api.Error.Code : "email_unavailable");
            throw;
        }
        await UpdateAsync("submitted", null);

        async Task UpdateAsync(string status, string? detail)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // Do not overwrite a delivery event imported while SendAsync was finishing.
                await db.MailLogs.Where(x => x.Id == requestId && x.Status == "submitting")
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, status)
                        .SetProperty(x => x.Detail, detail)
                        .SetProperty(x => x.UpdatedAt, clock.GetUtcNow().ToUnixTimeSeconds()), timeout.Token);
            }
            catch (Exception error) { logger.LogError("Mail log update failed ({ErrorType}) for {RequestId}", error.GetType().Name, requestId); }
        }
    }
}
