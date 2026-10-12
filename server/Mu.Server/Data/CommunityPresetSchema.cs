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
        var hasThumbnail = false;
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                if (reader.GetString(1) == "ThumbnailData") hasThumbnail = true;
        if (!hasThumbnail)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CommunityPresets ADD COLUMN ThumbnailData TEXT NULL");
    }
}
