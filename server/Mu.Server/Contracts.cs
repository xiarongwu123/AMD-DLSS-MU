namespace Mu.Server;

public sealed record CodeRequest(string Email, string Purpose);
public sealed record RegisterRequest(string Email, string Code, string Password, string TermsVersion, string? DeviceName);
public sealed record LoginRequest(string Email, string Password, string? DeviceName);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record AuthorizeRequest(string FeatureKey);
public sealed record CreateOrderRequest(string PlanId);
public sealed record MembershipSnapshot(string Tier, DateTimeOffset? ExpiresAt);
public sealed record FeatureSnapshot(string Key, bool Enabled, string MinimumTier, bool Allowed, long Version);
public sealed record AccountSnapshot(string Id, string Email, MembershipSnapshot Membership, IReadOnlyList<FeatureSnapshot> Features, bool PaymentsEnabled = false);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, AccountSnapshot Account);
public sealed record ApiError(string Code, string Message, int? RetryAfterSeconds = null);

public sealed class ApiException(int statusCode, string code, string message, int? retryAfterSeconds = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public ApiError Error { get; } = new(code, message, retryAfterSeconds);
}
