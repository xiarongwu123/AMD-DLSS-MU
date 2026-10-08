using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Services;

namespace Mu.Server.Data;

public static class DatabaseSetup
{
    public const int CurrentVersion = 3;
    public static async Task InitializeAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
        using var tables = db.Database.GetDbConnection().CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        var count = Convert.ToInt64(await tables.ExecuteScalarAsync());
        if (count == 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            db.DatabaseSchemas.Add(new() { Id = 1, Version = CurrentVersion });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        else
        {
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DatabaseSchemas';";
            if (Convert.ToInt64(await tables.ExecuteScalarAsync()) != 1)
                throw new InvalidOperationException("Unrecognized database. Use a separate empty account database; never point this service at the website database.");
            var schema = await db.DatabaseSchemas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
            if (schema?.Version is 1 or 2) await UpgradeAsync(db);
            else if (schema?.Version != CurrentVersion)
                throw new InvalidOperationException($"Unsupported account database schema {schema?.Version}; this server requires {CurrentVersion}. Back up and run the versioned upgrade before starting.");
        }
        await WishPoolSchema.InitializeAsync(db);
        var present = await db.FeatureDefinitions.Select(x => x.Key).ToListAsync();
        foreach (var key in AccountService.InitialFeatures.Except(present))
            db.FeatureDefinitions.Add(new() { Key = key, Enabled = true, MinimumTier = "standard", UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await CompatibilityCatalog.SeedAsync(db);
        await db.SaveChangesAsync();
        await db.Database.CloseConnectionAsync();
    }

    private static async Task UpgradeAsync(AppDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var schema = await db.DatabaseSchemas.SingleAsync(x => x.Id == 1);
        if (schema.Version == 1)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FeatureDefinitions\" ADD COLUMN \"Version\" INTEGER NOT NULL DEFAULT 1;");
            schema.Version = 2;
        }
        if (schema.Version == 2)
        {
            await db.Database.ExecuteSqlRawAsync(CompatibilitySchemaSql);
            schema.Version = 3;
        }
        if (schema.Version != CurrentVersion)
            throw new InvalidOperationException($"Unsupported account database schema {schema.Version}; this server requires {CurrentVersion}.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private const string CompatibilitySchemaSql = """
        CREATE TABLE "CompatibilityGames" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_CompatibilityGames" PRIMARY KEY,
            "Name" TEXT NOT NULL, "NormalizedName" TEXT NOT NULL, "SearchText" TEXT NOT NULL,
            "SteamAppId" TEXT NULL, "Developer" TEXT NULL, "Engine" TEXT NULL, "CreatedAt" INTEGER NOT NULL);
        CREATE UNIQUE INDEX "IX_CompatibilityGames_SteamAppId" ON "CompatibilityGames" ("SteamAppId");
        CREATE INDEX "IX_CompatibilityGames_NormalizedName" ON "CompatibilityGames" ("NormalizedName");
        CREATE TABLE "CompatibilityTests" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_CompatibilityTests" PRIMARY KEY,
            "UserId" TEXT NOT NULL, "SubmissionId" TEXT NOT NULL, "SubmissionHash" TEXT NOT NULL,
            "GameId" TEXT NOT NULL, "GpuName" TEXT NOT NULL, "GpuKey" TEXT NOT NULL,
            "EnvironmentJson" TEXT NOT NULL, "EnvironmentHash" TEXT NOT NULL, "Result" TEXT NOT NULL,
            "FailureReason" TEXT NULL, "Notes" TEXT NULL, "CreatedAt" INTEGER NOT NULL, "TestDay" INTEGER NOT NULL,
            CONSTRAINT "FK_CompatibilityTests_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_CompatibilityTests_CompatibilityGames_GameId" FOREIGN KEY ("GameId") REFERENCES "CompatibilityGames" ("Id") ON DELETE RESTRICT);
        CREATE UNIQUE INDEX "IX_CompatibilityTests_UserId_SubmissionId" ON "CompatibilityTests" ("UserId", "SubmissionId");
        CREATE UNIQUE INDEX "IX_CompatibilityTests_UserId_GameId_EnvironmentHash_TestDay" ON "CompatibilityTests" ("UserId", "GameId", "EnvironmentHash", "TestDay");
        CREATE INDEX "IX_CompatibilityTests_UserId_CreatedAt" ON "CompatibilityTests" ("UserId", "CreatedAt");
        CREATE INDEX "IX_CompatibilityTests_GameId_GpuKey_CreatedAt" ON "CompatibilityTests" ("GameId", "GpuKey", "CreatedAt");
        """;

    public static async Task BackupAsync(DbContext db, string destination)
    {
        var path = Path.GetFullPath(destination);
        if (File.Exists(path)) throw new InvalidOperationException("Backup destination already exists; choose a new file.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.partial");
        var sourceOpened = false;
        try
        {
            await db.Database.OpenConnectionAsync();
            sourceOpened = true;
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString()))
            {
                await target.OpenAsync();
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(target);
                using var check = target.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(await check.ExecuteScalarAsync() as string, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SQLite backup integrity check failed.");
                await target.CloseAsync();
            }
            // Same-directory move publishes only a fully closed, verified snapshot.
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (sourceOpened) await db.Database.CloseConnectionAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(temporary + suffix)) File.Delete(temporary + suffix);
        }
    }
}
