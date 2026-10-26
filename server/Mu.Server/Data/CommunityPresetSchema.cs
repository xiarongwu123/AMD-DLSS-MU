using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

public static class CommunityPresetSchema
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "CommunityPresets" (
            "Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL,
            "SubmissionId" TEXT NOT NULL, "GameKey" TEXT NOT NULL,
            "GameName" TEXT NOT NULL, "Mode" INTEGER NOT NULL,
            "Goal" TEXT NOT NULL, "Vulkan" INTEGER NOT NULL,
            "ComponentVersion" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Description" TEXT NOT NULL,
            "Author" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL,
            "SettingsJson" TEXT NOT NULL, "ImageData" TEXT NULL, "ThumbnailData" TEXT NULL,
            FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommunityPresets_User_Submission"
            ON "CommunityPresets" ("UserId", "SubmissionId");
        CREATE INDEX IF NOT EXISTS "IX_CommunityPresets_Game_Created"
            ON "CommunityPresets" ("GameKey", "CreatedAt");
        """);
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(CommunityPresets)";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        await using var transaction = await db.Database.BeginTransactionAsync();
        // These migration statements are fixed source literals, never request data.
        foreach (var (name, sql) in new[] {
            ("ThumbnailData", "ALTER TABLE CommunityPresets ADD COLUMN ThumbnailData TEXT NULL"),
            ("Scope", "ALTER TABLE CommunityPresets ADD COLUMN Scope TEXT NOT NULL DEFAULT 'game'"),
            ("ResourceUrl", "ALTER TABLE CommunityPresets ADD COLUMN ResourceUrl TEXT NULL"),
            ("ResourceCode", "ALTER TABLE CommunityPresets ADD COLUMN ResourceCode TEXT NULL"),
            ("AttachmentName", "ALTER TABLE CommunityPresets ADD COLUMN AttachmentName TEXT NULL"),
            ("AttachmentSize", "ALTER TABLE CommunityPresets ADD COLUMN AttachmentSize INTEGER NOT NULL DEFAULT 0"),
            ("AttachmentSha256", "ALTER TABLE CommunityPresets ADD COLUMN AttachmentSha256 TEXT NULL") })
            if (!columns.Contains(name)) await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CommunityPresetAttachments" (
              "PresetId" TEXT NOT NULL PRIMARY KEY, "Content" BLOB NOT NULL,
              FOREIGN KEY ("PresetId") REFERENCES "CommunityPresets" ("Id") ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS "IX_CommunityPresets_CreatedAt" ON "CommunityPresets" ("CreatedAt");
            """);
        await transaction.CommitAsync();
    }
}
