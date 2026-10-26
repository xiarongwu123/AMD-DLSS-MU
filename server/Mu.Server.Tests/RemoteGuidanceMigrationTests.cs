using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class RemoteGuidanceMigrationTests
{
    public static async Task<int> RunAsync(string directory)
    {
        var count=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Guidance upgrade: "+message);count++;}
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(directory,"legacy-v5-guidance.sqlite")}").Options;
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using(var db=new AppDbContext(options))
        {
            await DatabaseSetup.InitializeAsync(db);
            var plan=await db.Plans.SingleAsync(x=>x.Id=="mu-pro-lifetime");plan.PriceMinor=4990;plan.ConfigurationSessions=0;
            db.Plans.Remove(await db.Plans.SingleAsync(x=>x.Id=="mu-remote-once"));
            db.Users.Add(new(){Id="v5-user",UserName="v5-player",Email="v5@example.test"});
            db.Orders.Add(new(){Id="v5-lifetime",UserId="v5-user",PlanId=plan.Id,PlanName=plan.Name,AmountMinor=4990,DurationDays=0,CreatedAt=now,Provider="manual"});
            db.Orders.Add(new(){Id="v5-completed",UserId="v5-user",PlanId="mu-pro-assisted",PlanName="高级 Plus 会员",AmountMinor=2990,DurationDays=30,ConfigurationSessions=1,
                ConfigurationServiceStatus="completed",Status="paid",PaidAt=now-40*86400,CreatedAt=now-40*86400,Provider="manual"});
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Plans DROP COLUMN GrantsMembership;");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Orders DROP COLUMN GrantsMembership;");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Orders DROP COLUMN ConfigurationSessionsUsed;");
            await db.Database.ExecuteSqlRawAsync("UPDATE DatabaseSchemas SET Version=5;");
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER guidance_upgrade_failure BEFORE UPDATE ON DatabaseSchemas BEGIN SELECT RAISE(ABORT,'test rollback'); END;");
        }
        await using(var failed=new AppDbContext(options))
        {
            try{await DatabaseSetup.InitializeAsync(failed);throw new Exception("Expected migration failure");}
            catch(SqliteException){ }
            catch(DbUpdateException e) when(e.InnerException is SqliteException){ }
            await failed.Database.OpenConnectionAsync();
            using var command=failed.Database.GetDbConnection().CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM pragma_table_info('Orders') WHERE name='ConfigurationSessionsUsed';";
            Check(Convert.ToInt32(await command.ExecuteScalarAsync())==0,"failed upgrade rolls back added columns");
            command.CommandText="SELECT Version FROM DatabaseSchemas;";Check(Convert.ToInt32(await command.ExecuteScalarAsync())==5,"failed upgrade leaves version five");
            await failed.Database.ExecuteSqlRawAsync("DROP TRIGGER guidance_upgrade_failure;");
        }
        await using(var db=new AppDbContext(options))
        {
            await DatabaseSetup.InitializeAsync(db);
            var plan=await db.Plans.AsNoTracking().SingleAsync(x=>x.Id=="mu-pro-lifetime");
            Check(plan.PriceMinor==8880&&plan.ConfigurationSessions==3&&plan.GrantsMembership,"upgrades existing lifetime offer to 88.8 and three sessions");
            var old=await db.Orders.AsNoTracking().SingleAsync(x=>x.Id=="v5-lifetime");
            Check(old.AmountMinor==4990&&old.ConfigurationSessions==0&&old.GrantsMembership,"historical 49.9 order retains original price and entitlement");
            var completed=await db.Orders.AsNoTracking().SingleAsync(x=>x.Id=="v5-completed");
            Check(completed.ConfigurationSessionsUsed==1&&completed.ConfigurationServiceStatus=="completed","completed historical service is not credited a second time");
            Check(await MembershipPlans.RemoteSessionsRemainingAsync(db,"v5-user")==0,"unpaid and already completed orders grant no guidance balance");
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ManualPayment:Enabled","true"},{"ManualPayment:Instructions","test payment"}}).Build();
            var orders=new ManualOrderService(db,config,TimeProvider.System);
            var updated=await orders.CreateAsync("v5-user",plan.Id);
            Check(updated.Id!=old.Id&&updated.AmountMinor==8880&&updated.ConfigurationSessions==3,"new offer does not reuse a stale pending 49.9 order");
            await orders.ConfirmAsync(old.Id,"legacy-lifetime-reference","confirm old agreed entitlement","test-admin");
            Check(await MembershipPlans.RemoteSessionsRemainingAsync(db,"v5-user")==0,"confirming a historical lifetime order does not add three unpurchased credits");
            await DatabaseSetup.InitializeAsync(db);
            Check(await db.Plans.CountAsync(x=>x.Id=="mu-remote-once")==1&&await db.AuditEntries.CountAsync(x=>x.Action=="plans.remote_guidance_upgrade")==1,"restart neither duplicates new plans nor reapplies migration");
        }
        return count;
    }
}
