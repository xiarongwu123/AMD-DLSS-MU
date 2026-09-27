using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class AccountService(AppDbContext db, UserManager<ApplicationUser> users,
    IVerificationEmailSender emailSender, IConfiguration config, TimeProvider clock)
{
    public const string TermsVersion = "2026-09-26";
    public static readonly string[] InitialFeatures = ["library.manage", "dlss.configure", "optiscaler.configure",
        "magpie.launch", "game.launch", "game.restore", "configuration.edit", "diagnostics.use", "app.update"];
    public long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Email(string input)
    {
        input = input?.Trim() ?? "";
        if (input.Length > 254 || !MailAddress.TryCreate(input, out var address) || address.Address != input || !input.Contains('@'))
            throw new ApiException(400, "invalid_email", "请输入有效的邮箱地址。");
        return input.ToLowerInvariant();
    }

    private static void Password(string value)
    {
        if (value is null || value.Length < 8 || value.Length > 128)
            throw new ApiException(400, "invalid_password", "密码长度须为 8 至 128 个字符。");
    }

    private void RequireRegistration()
    {
        if (!config.GetValue<bool>("Registration:Enabled"))
            throw new ApiException(503, "registration_unavailable", "注册服务暂未开放，请稍后重试。");
    }

    private string CodeHash(string id, string email, string purpose, string code)
    {
        var pepper = config["Auth:CodePepper"] ?? "";
        if (Encoding.UTF8.GetByteCount(pepper) < 32)
            throw new ApiException(503, "email_unavailable", "验证码服务暂未配置。");
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper),
            Encoding.UTF8.GetBytes($"{id}\n{email}\n{purpose}\n{code}")));
    }

    public async Task RequestCodeAsync(CodeRequest input, CancellationToken cancellationToken = default)
    {
        var email = Email(input.Email);
        if (input.Purpose is not ("register" or "reset"))
            throw new ApiException(400, "invalid_purpose", "验证码用途无效。");
        if (input.Purpose == "register") RequireRegistration();
        var now = Now;
        VerificationCode challenge;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var recent = await db.VerificationCodes.Where(x => x.Email == email && x.CreatedAt > now - 3600)
                .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
            if (recent.Count >= 5)
                throw new ApiException(429, "code_rate_limited", "发送过于频繁，请稍后再试。", (int)(recent.Min(x => x.CreatedAt) + 3600 - now));
            if (recent.Count != 0 && recent[0].CreatedAt > now - 60)
                throw new ApiException(429, "code_rate_limited", "请稍后再获取验证码。", (int)(recent[0].CreatedAt + 60 - now));
            foreach (var old in await db.VerificationCodes.Where(x => x.Email == email && x.Purpose == input.Purpose && x.ConsumedAt == null).ToListAsync(cancellationToken))
                old.ConsumedAt = now;
            challenge = new() { Email = email, Purpose = input.Purpose, CreatedAt = now, ExpiresAt = now + 600 };
            challenge.CodeHash = CodeHash(challenge.Id, email, input.Purpose, code);
            db.VerificationCodes.Add(challenge);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        var user = await users.FindByEmailAsync(email);
        // A generic response avoids disclosing whether an email has an account.
        if ((input.Purpose == "register" && user != null) || (input.Purpose == "reset" && (user == null || user.DisabledAt != null))) return;
        await emailSender.SendAsync(email, code, input.Purpose, challenge.Id, cancellationToken);
        challenge.DeliverySucceeded = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ConsumeCodeAsync(string email, string purpose, string code)
    {
        var now = Now;
        var challenge = await db.VerificationCodes.Where(x => x.Email == email && x.Purpose == purpose && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        if (challenge == null || !challenge.DeliverySucceeded || challenge.ExpiresAt <= now || challenge.Attempts >= 5) return false;
        challenge.Attempts++;
        var valid = code is { Length: 6 } && code.All(char.IsAsciiDigit) && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(challenge.CodeHash), Convert.FromHexString(CodeHash(challenge.Id, email, purpose, code)));
        if (valid || challenge.Attempts >= 5) challenge.ConsumedAt = now;
        await db.SaveChangesAsync();
        return valid;
    }

    public async Task<SessionResponse> RegisterAsync(RegisterRequest input)
    {
        RequireRegistration();
        var email = Email(input.Email);
        Password(input.Password);
        if (input.TermsVersion != TermsVersion)
            throw new ApiException(400, "terms_required", "请阅读并同意当前版本的用户协议和隐私政策。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await users.FindByEmailAsync(email) != null)
            throw new ApiException(409, "email_registered", "该邮箱已注册，请登录或找回密码。");
        if (!await ConsumeCodeAsync(email, "register", input.Code))
        {
            await transaction.CommitAsync();
            throw new ApiException(400, "invalid_code", "验证码无效、已使用或已过期。");
        }
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = Now,
            TermsVersion = TermsVersion, TermsAcceptedAt = Now, LockoutEnabled = true };
        var created = await users.CreateAsync(user, input.Password);
        RequireIdentity(created);
        var session = await IssueSessionAsync(user, input.DeviceName);
        await transaction.CommitAsync();
        return session;
    }

    public async Task<SessionResponse> LoginAsync(LoginRequest input)
    {
        var email = Email(input.Email);
        Password(input.Password);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByEmailAsync(email);
        if (user == null) throw new ApiException(401, "invalid_credentials", "邮箱或密码错误。");
        if (await users.IsLockedOutAsync(user)) throw new ApiException(429, "login_rate_limited", "尝试过多，请 15 分钟后重试。", 900);
        if (!await users.CheckPasswordAsync(user, input.Password))
        {
            await users.AccessFailedAsync(user);
            await transaction.CommitAsync();
            throw new ApiException(401, "invalid_credentials", "邮箱或密码错误。");
        }
        if (user.DisabledAt != null) throw new ApiException(403, "account_disabled", "账号已被停用。");
        if (!user.EmailConfirmed) throw new ApiException(403, "email_unverified", "请先验证邮箱。");
        await users.ResetAccessFailedCountAsync(user);
        var session = await IssueSessionAsync(user, input.DeviceName);
        await transaction.CommitAsync();
        return session;
    }

    private async Task<SessionResponse> IssueSessionAsync(ApplicationUser user, string? deviceName, string? familyId = null, long? familyExpiresAt = null)
    {
        var now = Now;
        var access = Token();
        var refresh = Token();
        var session = new AuthSession { UserId = user.Id, FamilyId = familyId ?? Guid.NewGuid().ToString("N"),
            AccessHash = HashToken(access), RefreshHash = HashToken(refresh), CreatedAt = now,
            AccessExpiresAt = now + 900, RefreshExpiresAt = familyExpiresAt ?? now + 30 * 86400,
            DeviceName = (deviceName ?? "MU Client").Trim()[..Math.Min((deviceName ?? "MU Client").Trim().Length, 120)] };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();
        return new(access, refresh, DateTimeOffset.FromUnixTimeSeconds(session.AccessExpiresAt), await SnapshotAsync(user));
    }

    public async Task<SessionResponse> RefreshAsync(RefreshRequest input)
    {
        if (input.RefreshToken is not { Length: >= 32 and <= 256 }) throw InvalidSession();
        var hash = HashToken(input.RefreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var old = await db.AuthSessions.SingleOrDefaultAsync(x => x.RefreshHash == hash);
        if (old == null) throw InvalidSession();
        if (old.RotatedAt != null || old.RevokedAt != null)
        {
            await RevokeFamilyAsync(old.FamilyId);
            await transaction.CommitAsync();
            throw InvalidSession();
        }
        var user = await users.FindByIdAsync(old.UserId);
        if (user == null || old.RefreshExpiresAt <= Now) throw InvalidSession();
        if (user.DisabledAt != null) throw new ApiException(403, "account_disabled", "账号已被停用。");
        old.RotatedAt = Now;
        old.RevokedAt = Now;
        var session = await IssueSessionAsync(user, old.DeviceName, old.FamilyId, old.RefreshExpiresAt);
        await transaction.CommitAsync();
        return session;
    }

    public Task RevokeFamilyAsync(string familyId) => db.AuthSessions.Where(x => x.FamilyId == familyId && x.RevokedAt == null)
        .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, (long?)Now));

    public async Task ResetPasswordAsync(ResetPasswordRequest input)
    {
        var email = Email(input.Email);
        Password(input.NewPassword);
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await ConsumeCodeAsync(email, "reset", input.Code))
        {
            await transaction.CommitAsync();
            throw new ApiException(400, "invalid_code", "验证码无效、已使用或已过期。");
        }
        var user = await users.FindByEmailAsync(email);
        if (user == null || user.DisabledAt != null) throw new ApiException(400, "invalid_code", "验证码无效、已使用或已过期。");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        RequireIdentity(await users.ResetPasswordAsync(user, token, input.NewPassword));
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await RevokeUserSessionsAsync(user.Id);
        await transaction.CommitAsync();
    }

    public async Task ChangePasswordAsync(string userId, ChangePasswordRequest input)
    {
        Password(input.NewPassword);
        if (input.CurrentPassword is not { Length: >= 1 and <= 128 }) throw new ApiException(400, "invalid_password", "当前密码错误。");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(userId) ?? throw InvalidSession();
        if (user.DisabledAt != null) throw new ApiException(403, "account_disabled", "账号已被停用。");
        RequireIdentity(await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword));
        await RevokeUserSessionsAsync(user.Id);
        await transaction.CommitAsync();
    }

    public Task RevokeUserSessionsAsync(string userId) => db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null)
        .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, (long?)Now));

    public async Task<AccountSnapshot> SnapshotAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId) ?? throw InvalidSession();
        if (user.DisabledAt != null) throw new ApiException(403, "account_disabled", "账号已被停用。");
        return await SnapshotAsync(user);
    }

    private async Task<AccountSnapshot> SnapshotAsync(ApplicationUser user)
    {
        var pro = user.ProExpiresAt > Now;
        var features = await db.FeatureDefinitions.AsNoTracking().OrderBy(x => x.Key).ToListAsync();
        return new(user.Id, user.Email!, new(pro ? "pro" : "standard", user.ProExpiresAt is long expiry ? DateTimeOffset.FromUnixTimeSeconds(expiry) : null),
            features.Select(f => new FeatureSnapshot(f.Key, f.Enabled, f.MinimumTier, f.Enabled && (f.MinimumTier == "standard" || pro && f.MinimumTier == "pro"), f.Version)).ToArray());
    }

    private static ApiException InvalidSession() => new(401, "invalid_session", "登录已失效，请重新登录。");
    private static void RequireIdentity(IdentityResult result)
    {
        if (!result.Succeeded) throw new ApiException(400, "invalid_password", "密码不符合要求或当前密码错误。");
    }
}
