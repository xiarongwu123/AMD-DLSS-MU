using Microsoft.EntityFrameworkCore;
namespace Mu.Server.Data;

public static class MembershipPlans
{
    // A permanent expiry sentinel keeps older clients and existing tier checks compatible.
    public const long LifetimeExpiry = 253402300799;
    public static async Task InstallAsync(AppDbContext db)
    {
        var existing = await db.Plans.ToListAsync();
        foreach (var plan in existing) plan.Enabled = false;
        var defaults = new[] {
            new Plan { Id = "mu-pro-monthly", Name = "高级会员", DurationDays = 30, PriceMinor = 990, Enabled = true },
            new Plan { Id = "mu-pro-lifetime", Name = "终身会员", DurationDays = 0, ConfigurationSessions = 3, PriceMinor = 8880, Enabled = true },
            new Plan { Id = "mu-pro-assisted", Name = "高级 Plus 会员", DurationDays = 30, ConfigurationSessions = 1, PriceMinor = 2990, Enabled = true },
            RemotePlan()
        };
        foreach (var plan in defaults)
        {
            if (existing.Any(x => x.Id == plan.Id)) throw new InvalidOperationException("Reserved membership plan ID already exists; review the account database before upgrading.");
            db.Plans.Add(plan);
        }
        db.AuditEntries.Add(new AuditEntry { Actor = "system", Action = "plans.catalog_upgrade", Reason = "设置9.9月会员、88.8终身会员含3次远程指导、29.9 Plus及30元单次远程指导；旧订单保持原权益", CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    private static Plan RemotePlan() => new() { Id = "mu-remote-once", Name = "远程指导服务", DurationDays = 0,
        ConfigurationSessions = 1, GrantsMembership = false, PriceMinor = 3000, Enabled = true };

    public static async Task UpgradeRemoteGuidanceAsync(AppDbContext db)
    {
        var lifetime = await db.Plans.SingleOrDefaultAsync(x => x.Id == "mu-pro-lifetime");
        var before = lifetime == null ? "null" : System.Text.Json.JsonSerializer.Serialize(lifetime);
        if (lifetime != null) { lifetime.PriceMinor = 8880; lifetime.ConfigurationSessions = 3; lifetime.GrantsMembership = true; }
        if (!await db.Plans.AnyAsync(x => x.Id == "mu-remote-once")) db.Plans.Add(RemotePlan());
        db.AuditEntries.Add(new AuditEntry { Actor = "system", Action = "plans.remote_guidance_upgrade", BeforeJson = before,
            AfterJson = lifetime == null ? "null" : System.Text.Json.JsonSerializer.Serialize(lifetime),
            Reason = "终身会员改88.8元含3次远程指导；新增30元单次指导，不含会员；保留Plus和历史订单权益", CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    public static async Task<int> RemoteSessionsRemainingAsync(AppDbContext db, string userId) =>
        await db.Orders.AsNoTracking().Where(x => x.UserId == userId && x.Status == "paid" && x.PaidAt != null
            && x.ConfigurationSessions > 0 && x.ConfigurationSessionsUsed >= 0 && x.ConfigurationSessionsUsed <= x.ConfigurationSessions)
            .SumAsync(x => x.ConfigurationSessions - x.ConfigurationSessionsUsed);

    public static async Task<long?> PlusExpiresAtAsync(AppDbContext db, string userId)
    {
        // Use confirmed order snapshots, never mutable plan names or an unpaid order.
        // Plus renewals extend Plus time independently of ordinary/lifetime software access.
        var orders = await db.Orders.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == "paid" && x.PaidAt != null
                && x.GrantsMembership && x.ConfigurationSessions > 0 && x.DurationDays > 0 && x.DurationDays <= 3650)
            .OrderBy(x => x.PaidAt).ThenBy(x => x.Id)
            .Select(x => new { x.PaidAt, x.DurationDays }).ToListAsync();
        long? expiry = null;
        foreach (var order in orders)
            expiry = Math.Min(LifetimeExpiry, Math.Max(expiry ?? 0, order.PaidAt!.Value) + order.DurationDays * 86400L);
        return expiry;
    }
}
