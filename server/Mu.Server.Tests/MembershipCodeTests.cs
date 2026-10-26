using Microsoft.EntityFrameworkCore;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

static class MembershipCodeTests
{
    public static async Task RunAsync(string directory)
    {
        var path = Path.Combine(directory, "codes.sqlite");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Default Timeout=20").Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        // Additive upgrade is safe on both an existing account schema and repeated startup.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE MembershipCodes;");
        await MembershipCodeSchema.InitializeAsync(db);
        await MembershipCodeSchema.InitializeAsync(db);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.Users.AddRange(new ApplicationUser { Id = "a", ProExpiresAt = now + 10 * 86400 }, new ApplicationUser { Id = "b" },
            new ApplicationUser { Id = "disabled", DisabledAt = now });
        await db.SaveChangesAsync();
        var service = new MembershipCodeService(db, TimeProvider.System);
        var codes = await service.GenerateAsync(30, 5, 90, "admin", "test batch");
        var before = (await db.Users.SingleAsync(x => x.Id == "a")).ProExpiresAt;
        await service.RedeemAsync("a", " " + codes[0].ToLowerInvariant() + " ");
        Check((await db.Users.SingleAsync(x => x.Id == "a")).ProExpiresAt == before + 30 * 86400L, "existing expiry extended");
        await Unavailable(() => service.RedeemAsync("b", codes[0]));
        await service.RedeemAsync("b", codes[1]);
        Check((await db.Users.SingleAsync(x => x.Id == "b")).ProExpiresAt >= now + 30 * 86400L, "standard account activated");
        var rows = await db.MembershipCodes.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Check(rows.All(x => !codes.Contains(x.CodeHash)), "no plaintext codes stored");
        try { await service.RedeemAsync("disabled", codes[2]); throw new Exception("disabled user accepted"); }
        catch (ApiException e) when (e.StatusCode == 403) { }
        db.ChangeTracker.Clear();
        await service.RedeemAsync("b", codes[2]);
        var unused = await db.MembershipCodes.AsNoTracking().Where(x => x.RedeemedAt == null).ToListAsync();
        await service.RevokeAsync(unused[0].Id, "admin", "test revoke");
        await db.MembershipCodes.Where(x => x.Id == unused[1].Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, now - 1));
        await Unavailable(() => service.RedeemAsync("a", codes[3]));
        await Unavailable(() => service.RedeemAsync("a", codes[4]));
        var race = (await service.GenerateAsync(7, 1, 1, "admin", "race test"))[0];
        async Task<bool> Redeem(string user)
        {
            await using var context = new AppDbContext(options);
            try { await new MembershipCodeService(context, TimeProvider.System).RedeemAsync(user, race); return true; }
            catch (ApiException e) when (e.Error.Code == "code_unavailable") { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Redeem("a")), Task.Run(() => Redeem("b")));
        Check(results.Count(x => x) == 1, "one concurrent redemption wins");
        Check(await db.MembershipChanges.CountAsync() == 4, "one membership record per redemption");
        Check(await db.AuditEntries.CountAsync(x => x.Action == "membership_code.redeem") == 4, "redemption audit recorded");
        Console.WriteLine("Membership CDK tests passed (extension, replay, disabled rollback, revoked/expired, concurrency, audit).");
    }
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("CDK FAILED: " + description);
    }
    private static async Task Unavailable(Func<Task> action)
    {
        try { await action(); }
        catch (ApiException e) when (e.Error.Code == "code_unavailable") { return; }
        throw new Exception("CDK FAILED: unavailable code accepted");
    }
}
