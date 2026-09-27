using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class AccountManagementService(AppDbContext db, AccountService accounts, TimeProvider clock)
{
    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    private static void Validate(string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || string.IsNullOrWhiteSpace(actor) || actor.Length > 254)
            throw new ApiException(400, "invalid_reason", "请填写操作原因（最多 1000 字）。");
    }
    private async Task<ApplicationUser> User(string id) => await db.Users.SingleOrDefaultAsync(x => x.Id == id)
        ?? throw new ApiException(404, "user_not_found", "用户不存在。");
    private void Audit(string actor, string action, string? userId, string reason, object before, object after) => db.AuditEntries.Add(new()
    {
        Actor = actor, Action = action, UserId = userId, Reason = reason.Trim(),
        BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after), CreatedAt = Now
    });

    public async Task SetDisabledAsync(string userId, bool disabled, string reason, string actor)
    {
        Validate(reason, actor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await User(userId);
        var before = user.DisabledAt;
        user.DisabledAt = disabled ? Now : null;
        if (disabled)
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await accounts.RevokeUserSessionsAsync(userId);
        }
        Audit(actor, disabled ? "user.disable" : "user.enable", userId, reason, new { DisabledAt = before }, new { user.DisabledAt });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetProExpiryAsync(string userId, long? expiresAt, string reason, string actor)
    {
        Validate(reason, actor);
        if (expiresAt is < 0 or > 253402300799) throw new ApiException(400, "invalid_expiry", "会员到期时间无效。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await SetExpiry(await User(userId), expiresAt, reason, actor);
        await transaction.CommitAsync();
    }

    public async Task GrantProDaysAsync(string userId, int days, string reason, string actor)
    {
        Validate(reason, actor);
        if (days is < 1 or > 3650) throw new ApiException(400, "invalid_duration", "开通时长须为 1 至 3650 天。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await User(userId);
        var expiry = checked(Math.Max(user.ProExpiresAt ?? 0, Now) + days * 86400L);
        if (expiry > 253402300799) throw new ApiException(400, "invalid_expiry", "会员到期时间无效。");
        await SetExpiry(user, expiry, reason, actor);
        await transaction.CommitAsync();
    }

    private async Task SetExpiry(ApplicationUser user, long? expiresAt, string reason, string actor)
    {
        var previous = user.ProExpiresAt;
        user.ProExpiresAt = expiresAt;
        db.MembershipChanges.Add(new() { UserId = user.Id, PreviousExpiresAt = previous, NewExpiresAt = expiresAt,
            Reason = reason.Trim(), Actor = actor, CreatedAt = Now });
        Audit(actor, "membership.expiry", user.Id, reason, new { ProExpiresAt = previous }, new { ProExpiresAt = expiresAt });
        await db.SaveChangesAsync();
    }

    public async Task RevokeSessionsAsync(string userId, string reason, string actor)
    {
        Validate(reason, actor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await User(userId);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await accounts.RevokeUserSessionsAsync(userId);
        Audit(actor, "sessions.revoke", userId, reason, new { }, new { AllSessionsRevoked = true });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetFeatureAsync(string key, bool enabled, string minimumTier, string reason, string actor)
    {
        Validate(reason, actor);
        if (minimumTier is not ("standard" or "pro")) throw new ApiException(400, "invalid_tier", "会员等级无效。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var feature = await db.FeatureDefinitions.SingleOrDefaultAsync(x => x.Key == key)
            ?? throw new ApiException(404, "feature_not_found", "功能不存在。");
        var before = new { feature.Enabled, feature.MinimumTier, feature.Version };
        feature.Enabled = enabled;
        feature.MinimumTier = minimumTier;
        feature.Version = checked(feature.Version + 1);
        feature.UpdatedAt = Now;
        Audit(actor, "feature.configure", null, reason, new { Key = key, Value = before }, new { Key = key, feature.Enabled, feature.MinimumTier, feature.Version });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
