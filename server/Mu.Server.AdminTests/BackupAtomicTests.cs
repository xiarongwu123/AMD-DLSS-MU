using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server.Data;

internal static class BackupAtomicTests
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        var directory = Path.Combine(Path.GetTempPath(), "mu-backup-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var originalUsers = await db.Users.CountAsync();
            var destination = Path.Combine(directory, "accounts-20260926T000000Z.sqlite");
            await DatabaseSetup.BackupAsync(db, destination);
            Check(File.Exists(destination), "Completed snapshot published at final path");
            Check(Directory.GetFiles(directory).Length == 1, "Successful backup has no temporary/WAL/SHM files");
            await using (var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = destination, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await snapshot.OpenAsync();
                using var query = snapshot.CreateCommand();
                query.CommandText = "PRAGMA integrity_check;";
                Check(await query.ExecuteScalarAsync() as string == "ok", "Published snapshot passes independent integrity check");
                query.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
                Check(Convert.ToInt64(await query.ExecuteScalarAsync()) == originalUsers, "Closed published snapshot contains all accounts");
            }
            var priorBytes = await File.ReadAllBytesAsync(destination);
            try { await DatabaseSetup.BackupAsync(db, destination); throw new Exception("Existing backup was overwritten"); }
            catch (InvalidOperationException error) when (error.Message.Contains("already exists", StringComparison.Ordinal)) { assertions++; }
            var retainedBytes = await File.ReadAllBytesAsync(destination);
            Check(priorBytes.SequenceEqual(retainedBytes), "Existing snapshot remains unchanged");

            // A directory at the final path forces publication to fail after copying and validation.
            var conflict = Path.Combine(directory, "accounts-20260926T000001Z.sqlite");
            Directory.CreateDirectory(conflict);
            try { await DatabaseSetup.BackupAsync(db, conflict); throw new Exception("Conflicting destination accepted"); }
            catch (IOException) { assertions++; }
            Check(!Directory.EnumerateFiles(directory, "*.partial*").Any(), "Failed publication removes its partial database and sidecars");
            Check(Directory.Exists(conflict) && !File.Exists(conflict), "Failed publication never replaces existing destination");
            Check(await db.Users.CountAsync() == originalUsers, "Source remains usable after failed backup publication");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return assertions;
    }
}
