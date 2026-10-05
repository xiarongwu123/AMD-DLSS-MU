using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

// Operational logs stay separate so account-schema upgrades and rollbacks remain independent.
public sealed class MailLogDbContext(DbContextOptions<MailLogDbContext> options) : DbContext(options)
{
    public DbSet<MailLog> MailLogs => Set<MailLog>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<MailLog>().HasIndex(x => x.CreatedAt);
        builder.Entity<MailLog>().HasIndex(x => new { x.Email, x.CreatedAt });
    }
}

public sealed class MailLog
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Status { get; set; } = "submitting";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeliveryEventAt { get; set; }
    public string? QueueId { get; set; }
    public string? Detail { get; set; }
}
