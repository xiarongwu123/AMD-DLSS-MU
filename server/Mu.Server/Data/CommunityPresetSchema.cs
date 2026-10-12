using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

public static class CommunityPresetSchema
{
    public static Task InitializeAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "CommunityPresets" (
            "Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL,
            "SubmissionId" TEXT NOT NULL, "GameKey" TEXT NOT NULL,
            "GameName" TEXT NOT NULL, "Mode" INTEGER NOT NULL,
            "Goal" TEXT NOT NULL, "Vulkan" INTEGER NOT NULL,
            "ComponentVersion" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Description" TEXT NOT NULL,
            "Author" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL,
            "SettingsJson" TEXT NOT NULL, "ImageData" TEXT NULL,
            FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommunityPresets_User_Submission"
            ON "CommunityPresets" ("UserId", "SubmissionId");
        CREATE INDEX IF NOT EXISTS "IX_CommunityPresets_Game_Created"
            ON "CommunityPresets" ("GameKey", "CreatedAt");
        """);
}
