using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<DatabaseSchema> DatabaseSchemas => Set<DatabaseSchema>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<FeatureDefinition> FeatureDefinitions => Set<FeatureDefinition>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<MembershipChange> MembershipChanges => Set<MembershipChange>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<CompatibilityGameEntry> CompatibilityGames => Set<CompatibilityGameEntry>();
    public DbSet<CompatibilityTestEntry> CompatibilityTests => Set<CompatibilityTestEntry>();
    public DbSet<GameTelemetryEntry> GameTelemetry => Set<GameTelemetryEntry>();
    public DbSet<WishEntry> Wishes => Set<WishEntry>();
    public DbSet<WishVoteEntry> WishVotes => Set<WishVoteEntry>();
    public DbSet<WishCommentEntry> WishComments => Set<WishCommentEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<WishEntry>().HasIndex(x => new { x.UserId, x.SubmissionId }).IsUnique();
        builder.Entity<WishEntry>().HasIndex(x => x.CreatedAt);
        builder.Entity<WishEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WishVoteEntry>().HasKey(x => new { x.WishId, x.UserId });
        builder.Entity<WishVoteEntry>().HasOne<WishEntry>().WithMany().HasForeignKey(x => x.WishId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WishVoteEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WishCommentEntry>().HasIndex(x => new { x.WishId, x.CreatedAt });
        builder.Entity<WishCommentEntry>().HasOne<WishEntry>().WithMany().HasForeignKey(x => x.WishId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WishCommentEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<AuthSession>().HasIndex(x => x.AccessHash).IsUnique();
        builder.Entity<AuthSession>().HasIndex(x => x.RefreshHash).IsUnique();
        builder.Entity<AuthSession>().HasIndex(x => new { x.UserId, x.FamilyId });
        builder.Entity<AuthSession>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<VerificationCode>().HasIndex(x => new { x.Email, x.Purpose, x.CreatedAt });
        builder.Entity<FeatureDefinition>().HasKey(x => x.Key);
        builder.Entity<Order>().HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.Entity<Order>().HasIndex(x => new { x.Provider, x.ProviderTransactionId }).IsUnique();
        builder.Entity<Order>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Order>().HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaymentEvent>().HasIndex(x => new { x.Provider, x.TransactionId }).IsUnique();
        builder.Entity<PaymentEvent>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MembershipChange>().HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.Entity<AuditEntry>().HasIndex(x => x.CreatedAt);
        builder.Entity<CompatibilityGameEntry>().HasIndex(x => x.SteamAppId).IsUnique();
        builder.Entity<CompatibilityGameEntry>().HasIndex(x => x.NormalizedName);
        builder.Entity<CompatibilityTestEntry>().HasIndex(x => new { x.UserId, x.SubmissionId }).IsUnique();
        builder.Entity<CompatibilityTestEntry>().HasIndex(x => new { x.UserId, x.GameId, x.EnvironmentHash, x.TestDay }).IsUnique();
        builder.Entity<CompatibilityTestEntry>().HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.Entity<CompatibilityTestEntry>().HasIndex(x => new { x.GameId, x.GpuKey, x.CreatedAt });
        builder.Entity<CompatibilityTestEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CompatibilityTestEntry>().HasOne<CompatibilityGameEntry>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<GameTelemetryEntry>().HasKey(x => new { x.UserId, x.Id });
        builder.Entity<GameTelemetryEntry>().HasIndex(x => new { x.GameName, x.GpuName, x.MuMode, x.StartedAt });
        builder.Entity<GameTelemetryEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
