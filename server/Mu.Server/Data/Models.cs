using Microsoft.AspNetCore.Identity;

namespace Mu.Server.Data;

public sealed class DatabaseSchema
{
    public int Id { get; set; }
    public int Version { get; set; }
}

public sealed class ApplicationUser : IdentityUser
{
    public long CreatedAt { get; set; }
    public long? DisabledAt { get; set; }
    public long? ProExpiresAt { get; set; }
    public string TermsVersion { get; set; } = "";
    public long TermsAcceptedAt { get; set; }
}

public sealed class AuthSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public string AccessHash { get; set; } = "";
    public string RefreshHash { get; set; } = "";
    public long CreatedAt { get; set; }
    public long AccessExpiresAt { get; set; }
    public long RefreshExpiresAt { get; set; }
    public long? RotatedAt { get; set; }
    public long? RevokedAt { get; set; }
    public string DeviceName { get; set; } = "";
}

public sealed class VerificationCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public long? ConsumedAt { get; set; }
    public bool DeliverySucceeded { get; set; }
}

public sealed class FeatureDefinition
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string MinimumTier { get; set; } = "standard";
    public long Version { get; set; } = 1;
    public long UpdatedAt { get; set; }
}

public sealed class Plan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int DurationDays { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public bool Enabled { get; set; }
}

public sealed class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string PlanName { get; set; } = "";
    public int DurationDays { get; set; }
    public string Status { get; set; } = "pending";
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public long CreatedAt { get; set; }
    public long? PaidAt { get; set; }
    public string? Provider { get; set; }
    public string? ProviderTransactionId { get; set; }
}

public sealed class PaymentEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string? OrderId { get; set; }
    public long ReceivedAt { get; set; }
    public string Status { get; set; } = "received";
}

public sealed class MembershipChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public long? PreviousExpiresAt { get; set; }
    public long? NewExpiresAt { get; set; }
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Source { get; set; } = "manual";
    public long CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? UserId { get; set; }
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public long CreatedAt { get; set; }
}
