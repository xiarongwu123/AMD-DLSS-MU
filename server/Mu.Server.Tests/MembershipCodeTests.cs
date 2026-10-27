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
        db.Plans.AddRange(
            new Plan { Id="monthly", Name="高级会员", DurationDays=30, Enabled=true, PriceMinor=990 },
            new Plan { Id="life", Name="终身会员", DurationDays=0, ConfigurationSessions=3, Enabled=true, PriceMinor=8880 },
            new Plan { Id="plus", Name="高级Plus", DurationDays=30, ConfigurationSessions=1, Enabled=true, PriceMinor=2990 },
            new Plan { Id="remote", Name="单次指导", GrantsMembership=false, DurationDays=0, ConfigurationSessions=1, Enabled=true, PriceMinor=3000 });
        db.Users.AddRange(new ApplicationUser {Id="life-user"},new ApplicationUser {Id="plus-user"},new ApplicationUser {Id="remote-user"});
        await db.SaveChangesAsync();
        var lifeCode=(await service.GenerateAsync(30,1,90,"admin","lifetime test","life"))[0];
        try { await service.RedeemAsync("life-user",lifeCode,"plus"); throw new Exception("mismatched plan accepted"); }
        catch(ApiException e) when(e.Error.Code=="code_plan_mismatch") { }
        await service.RedeemAsync("life-user",lifeCode,"life");
        Check((await db.Users.SingleAsync(u=>u.Id=="life-user")).ProExpiresAt==MembershipPlans.LifetimeExpiry,"lifetime code grants permanent access");
        Check(await MembershipPlans.RemoteSessionsRemainingAsync(db,"life-user")==3,"lifetime code grants three service sessions");
        var plusCode=(await service.GenerateAsync(30,1,90,"admin","plus test","plus"))[0];
        // Snapshot grants remain fixed if the mutable catalogue is edited later.
        await db.Plans.Where(p=>p.Id=="plus").ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ConfigurationSessions,20));
        await service.RedeemAsync("plus-user",plusCode,"plus");
        Check(await MembershipPlans.RemoteSessionsRemainingAsync(db,"plus-user")==1,"Plus code keeps generated service snapshot");
        Check(await MembershipPlans.PlusExpiresAtAsync(db,"plus-user")>=now+30*86400L,"Plus code grants Plus membership time");
        var remoteCode=(await service.GenerateAsync(30,1,90,"admin","remote test","remote"))[0];
        await service.RedeemAsync("remote-user",remoteCode,"remote");
        Check((await db.Users.SingleAsync(u=>u.Id=="remote-user")).ProExpiresAt==null,"remote code does not grant membership");
        Check(await MembershipPlans.RemoteSessionsRemainingAsync(db,"remote-user")==1,"remote code grants one session");
        var oldCode=(await service.GenerateAsync(30,1,90,"admin","legacy mismatch"))[0];
        try { await service.RedeemAsync("b",oldCode,"life"); throw new Exception("old days code granted lifetime"); }
        catch(ApiException e) when(e.Error.Code=="code_plan_mismatch") { }
        await service.RedeemAsync("b",oldCode,"monthly");
        var lifeRenew=(await service.GenerateAsync(30,1,90,"admin","lifetime renewal"))[0];
        await service.RedeemAsync("life-user",lifeRenew);
        Check((await db.Users.SingleAsync(u=>u.Id=="life-user")).ProExpiresAt==MembershipPlans.LifetimeExpiry,"legacy renewal preserves lifetime");
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
