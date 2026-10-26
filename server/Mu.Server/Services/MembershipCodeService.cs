using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed record RedeemMembershipRequest(string Code);

public sealed class MembershipCodeService(AppDbContext db, TimeProvider clock)
{
    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
            throw new ApiException(400, "invalid_reason", "请填写 3 至 500 字的操作原因。");
    }

    public async Task<string[]> GenerateAsync(int days, int count, int validDays, string actor, string reason)
    {
        ValidateReason(reason);
        if (days is < 1 or > 3650 || count is < 1 or > 100 || validDays is < 1 or > 3650)
            throw new ApiException(400, "invalid_duration", "会员天数和兑换有效期须为 1 至 3650 天，每批 1 至 100 个码。");
        var now = Now;
        var codes = new List<string>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        for (var i = 0; i < count; i++)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var code = string.Join("-", Enumerable.Range(0, 8).Select(n => raw.Substring(n * 4, 4)));
            var item = new MembershipCode { CodeHash = Hash(raw), DurationDays = days, CreatedAt = now, ExpiresAt = now + validDays * 86400L };
            db.MembershipCodes.Add(item);
            Audit(actor, "membership_code.generate", null, reason, new { item.Id, days, item.ExpiresAt });
            codes.Add(code);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return codes.ToArray();
    }

    public async Task RevokeAsync(string id, string actor, string reason)
    {
        ValidateReason(reason);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = Now;
        var changed = await db.MembershipCodes.Where(x => x.Id == id && x.RedeemedAt == null && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        if (changed != 1) throw new ApiException(409, "code_unavailable", "兑换码不存在、已兑换或已停用。");
        Audit(actor, "membership_code.revoke", null, reason, new { Id = id });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RedeemAsync(string userId, string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 80)
            throw new ApiException(400, "invalid_code", "请输入有效的 CDK 兑换码。");
        var code = input.Trim().Replace("-", "").ToUpperInvariant();
        if (code.Length != 32 || code.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ApiException(400, "invalid_code", "请输入有效的 CDK 兑换码。");
        var hash = Hash(code);
        var now = Now;
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Claim with a conditional write before reading: concurrent requests cannot both consume a code.
        var changed = await db.MembershipCodes.Where(x => x.CodeHash == hash && x.RedeemedAt == null && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RedeemedAt, now).SetProperty(x => x.RedeemedBy, userId));
        if (changed != 1) throw new ApiException(409, "code_unavailable", "兑换码无效、已使用、已过期或已停用。");
        var item = await db.MembershipCodes.AsNoTracking().SingleAsync(x => x.CodeHash == hash);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId);
        if (user == null || user.DisabledAt != null) throw new ApiException(403, "account_disabled", "账户不可用。");
        var previous = user.ProExpiresAt;
        var expiry = Math.Max(previous ?? 0, now) + item.DurationDays * 86400L;
        if (expiry > 253402300799) throw new ApiException(400, "invalid_expiry", "会员到期时间超出允许范围。");
        user.ProExpiresAt = expiry;
        db.MembershipChanges.Add(new() { UserId = userId, PreviousExpiresAt = previous, NewExpiresAt = expiry,
            Actor = userId, Reason = "CDK:" + item.Id, CreatedAt = now });
        Audit(userId, "membership_code.redeem", userId, "CDK:" + item.Id, new { item.Id, PreviousExpiresAt = previous, NewExpiresAt = expiry });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void Audit(string actor, string action, string? userId, string reason, object after) => db.AuditEntries.Add(new()
    {
        Actor = actor, Action = action, UserId = userId, Reason = reason.Trim(), BeforeJson = "{}",
        AfterJson = JsonSerializer.Serialize(after), CreatedAt = Now
    });
}
