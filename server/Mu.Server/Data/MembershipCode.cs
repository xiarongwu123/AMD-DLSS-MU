using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

public sealed class MembershipCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CodeHash { get; set; } = "";
    public int DurationDays { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
    public long? RedeemedAt { get; set; }
    public string? RedeemedBy { get; set; }
    public long? RevokedAt { get; set; }
}

public static class MembershipCodeSchema
{
    public static Task InitializeAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "MembershipCodes" (
            "Id" TEXT NOT NULL PRIMARY KEY, "CodeHash" TEXT NOT NULL,
            "DurationDays" INTEGER NOT NULL, "CreatedAt" INTEGER NOT NULL, "ExpiresAt" INTEGER NOT NULL,
            "RedeemedAt" INTEGER NULL, "RedeemedBy" TEXT NULL, "RevokedAt" INTEGER NULL,
            FOREIGN KEY ("RedeemedBy") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MembershipCodes_CodeHash" ON "MembershipCodes" ("CodeHash");
        CREATE INDEX IF NOT EXISTS "IX_MembershipCodes_RedeemedBy" ON "MembershipCodes" ("RedeemedBy");
        """);
}
