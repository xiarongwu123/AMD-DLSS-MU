using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

await using var suite = new Suite();
await suite.RunAsync();

sealed class Suite : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mu-accounts-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock clock = new();
    private readonly TestMailer mailer = new();
    private readonly ServiceProvider services;
    private int assertions;
    public Suite()
    {
        Directory.CreateDirectory(directory);
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDataProtection().UseEphemeralDataProtectionProvider();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Registration:Enabled"] = "true", ["Auth:CodePepper"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
        }).Build());
        collection.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(directory, "accounts.sqlite")};Default Timeout=20"));
        collection.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Password.RequiredUniqueChars = 1;
            options.Password.RequireDigit = options.Password.RequireUppercase = options.Password.RequireLowercase = options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        collection.AddSingleton<TimeProvider>(clock);
        collection.AddSingleton<IVerificationEmailSender>(mailer);
        collection.AddScoped<AccountService>();
        collection.AddScoped<AccountManagementService>();
        collection.AddScoped<ManualOrderService>();
        collection.AddScoped<CompatibilityService>();
        services = collection.BuildServiceProvider();
    }

    private async Task<T> Scope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
    private Task Scope(Func<IServiceProvider, Task> action) => Scope(async provider => { await action(provider); return true; });
    private Task<T> Accounts<T>(Func<AccountService, Task<T>> action) => Scope(provider => action(provider.GetRequiredService<AccountService>()));
    private Task Accounts(Func<AccountService, Task> action) => Scope(provider => action(provider.GetRequiredService<AccountService>()));
    private void Assert(bool value, string description)
    {
        if (!value) throw new Exception("FAILED: " + description);
        assertions++;
    }
    private async Task Error(Func<Task> action, int status, string code)
    {
        try { await action(); }
        catch (ApiException error)
        {
            Assert(error.StatusCode == status && error.Error.Code == code, $"expected {status}/{code}, got {error.StatusCode}/{error.Error.Code}");
            return;
        }
        throw new Exception($"FAILED: expected {status}/{code}");
    }

    public async Task RunAsync()
    {
        await MembershipCodeTests.RunAsync(directory);
        await VerifyVersionOneUpgradeAsync();
        assertions += await RemoteGuidanceMigrationTests.RunAsync(directory);
        await Scope(provider => DatabaseSetup.InitializeAsync(provider.GetRequiredService<AppDbContext>()));
        const string email = "tester@example.com";
        const string password = "first-passphrase";
        await Accounts(a => a.RequestCodeAsync(new(email, "register")));
        var originalCode = mailer.Code(email, "register");
        Assert(originalCode.Length == 6 && originalCode.All(char.IsAsciiDigit), "six random digits sent");
        await Scope(async provider =>
        {
            var code = await provider.GetRequiredService<AppDbContext>().VerificationCodes.SingleAsync();
            Assert(code.CodeHash.Length == 64 && code.CodeHash != originalCode && code.DeliverySucceeded, "only code hash persisted after email accepted");
        });
        await Error(() => Accounts(a => a.RequestCodeAsync(new(email, "register"))), 429, "code_rate_limited");
        clock.Advance(61);
        await Accounts(a => a.RequestCodeAsync(new(email, "register")));
        var currentCode = mailer.Code(email, "register");
        await Scope(async provider =>
        {
            var rows = await provider.GetRequiredService<AppDbContext>().VerificationCodes.OrderBy(x => x.CreatedAt).ToListAsync();
            Assert(rows.Count == 2 && rows[0].ConsumedAt != null && rows[1].ConsumedAt == null, "resend invalidates old code");
        });
        var wrong = currentCode == "000000" ? "999999" : "000000";
        for (var i = 0; i < 5; i++)
            await Error(() => Accounts(a => a.RegisterAsync(new(email, wrong, password, AccountService.TermsVersion, "test"))), 400, "invalid_code");
        await Error(() => Accounts(a => a.RegisterAsync(new(email, currentCode, password, AccountService.TermsVersion, "test"))), 400, "invalid_code");
        await Scope(async provider =>
        {
            var latest = await provider.GetRequiredService<AppDbContext>().VerificationCodes.OrderByDescending(x => x.CreatedAt).FirstAsync();
            Assert(latest.Attempts == 5 && latest.ConsumedAt != null, "invalid attempt count commits and locks challenge");
        });
        clock.Advance(61);
        await Accounts(a => a.RequestCodeAsync(new(email, "register")));
        currentCode = mailer.Code(email, "register");
        var registration = new RegisterRequest(email, currentCode, password, AccountService.TermsVersion, "test");
        async Task<SessionResponse?> Register()
        {
            try { return await Accounts(a => a.RegisterAsync(registration)); }
            catch (ApiException error) when (error.StatusCode == 409 || error.Error.Code == "invalid_code") { return null; }
        }
        var simultaneous = await Task.WhenAll(Task.Run(Register), Task.Run(Register));
        Assert(simultaneous.Count(x => x != null) == 1, "concurrent code use creates exactly one account and session");
        var session = simultaneous.Single(x => x != null)!;
        var userId = session.Account.Id;
        Assert(session.Account.Membership.Tier == "standard" && session.Account.Features.Count == AccountService.InitialFeatures.Length && session.Account.Features.All(x => x.Allowed == (x.MinimumTier == "standard")), "new account has standard initial permissions");
        Assert(session.Account.Features.All(x => x.Version == 1), "fresh feature snapshots start at version one");
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            Assert(await db.Users.CountAsync() == 1 && await db.AuthSessions.CountAsync() == 1, "atomic registration has no duplicate rows");
            var stored = await db.AuthSessions.SingleAsync();
            Assert(stored.AccessHash != session.AccessToken && stored.RefreshHash != session.RefreshToken && stored.RefreshHash.Length == 64, "opaque tokens only stored as hashes");
            var user = await db.Users.SingleAsync();
            Assert(user.EmailConfirmed && user.PasswordHash != password && user.TermsVersion == AccountService.TermsVersion, "Identity password hash and verified terms persisted");
        });

        mailer.Fail = true;
        await Error(() => Accounts(a => a.RequestCodeAsync(new("delivery@example.com", "register"))), 503, "email_unavailable");
        mailer.Fail = false;
        await Scope(async provider => Assert(!await provider.GetRequiredService<AppDbContext>().VerificationCodes.Where(x => x.Email == "delivery@example.com").Select(x => x.DeliverySucceeded).SingleAsync(), "failed delivery never marked successful"));
        await Error(() => Accounts(a => a.RegisterAsync(new("delivery@example.com", "000000", password, AccountService.TermsVersion, null))), 400, "invalid_code");

        var refreshed = await Accounts(a => a.RefreshAsync(new(session.RefreshToken)));
        Assert(refreshed.RefreshToken != session.RefreshToken && refreshed.AccessToken != session.AccessToken, "refresh rotates both opaque tokens");
        await Scope(async provider => Assert((await provider.GetRequiredService<AppDbContext>().AuthSessions.SingleAsync(x => x.AccessHash == AccountService.HashToken(session.AccessToken))).RevokedAt != null, "rotation invalidates previous access token"));
        await Error(() => Accounts(a => a.RefreshAsync(new(session.RefreshToken))), 401, "invalid_session");
        await Error(() => Accounts(a => a.RefreshAsync(new(refreshed.RefreshToken))), 401, "invalid_session");
        Assert(await Scope(provider => provider.GetRequiredService<AppDbContext>().AuthSessions.AllAsync(x => x.RevokedAt != null)), "replay revokes every descendant in family");

        var login = await Accounts(a => a.LoginAsync(new(email, password, "desktop")));
        await Accounts(a => a.ChangePasswordAsync(userId, new(password, "second-passphrase")));
        await Error(() => Accounts(a => a.RefreshAsync(new(login.RefreshToken))), 401, "invalid_session");
        await Error(() => Accounts(a => a.LoginAsync(new(email, password, null))), 401, "invalid_credentials");
        login = await Accounts(a => a.LoginAsync(new(email, "second-passphrase", null)));
        await Scope(provider => provider.GetRequiredService<AccountManagementService>().SetDisabledAsync(userId, true, "test disable", "test-admin"));
        await Error(() => Accounts(a => a.LoginAsync(new(email, "second-passphrase", null))), 403, "account_disabled");
        await Error(() => Accounts(a => a.RefreshAsync(new(login.RefreshToken))), 401, "invalid_session");
        await Scope(provider => provider.GetRequiredService<AccountManagementService>().SetDisabledAsync(userId, false, "test enable", "test-admin"));
        await Error(() => Accounts(a => a.RefreshAsync(new(login.RefreshToken))), 401, "invalid_session");

        await Scope(provider => provider.GetRequiredService<AccountManagementService>().SetFeatureAsync("game.launch", true, "pro", "test tier", "test-admin"));
        Assert(!(await Accounts(a => a.SnapshotAsync(userId))).Features.Single(x => x.Key == "game.launch").Allowed, "standard cannot use Pro-only feature");
        var beforeGrant = clock.GetUtcNow().ToUnixTimeSeconds();
        await Task.WhenAll(Task.Run(() => Scope(provider => provider.GetRequiredService<AccountManagementService>().GrantProDaysAsync(userId, 30, "test grant", "test-admin"))),
            Task.Run(() => Scope(provider => provider.GetRequiredService<AccountManagementService>().GrantProDaysAsync(userId, 60, "test renewal", "test-admin"))));
        var pro = await Accounts(a => a.SnapshotAsync(userId));
        Assert(pro.Features.Single(x => x.Key == "game.launch").Version == 2, "first feature change publishes version two");
        Assert(pro.Membership.ExpiresAt?.ToUnixTimeSeconds() == beforeGrant + 90 * 86400L, "concurrent grants accumulate from max expiry and now");
        Assert(pro.Membership.Tier == "pro" && pro.Features.Single(x => x.Key == "game.launch").Allowed, "active Pro passes tier feature");
        await Scope(provider => provider.GetRequiredService<AccountManagementService>().SetFeatureAsync("game.launch", false, "pro", "test toggle", "test-admin"));
        Assert(!(await Accounts(a => a.SnapshotAsync(userId))).Features.Single(x => x.Key == "game.launch").Allowed, "global disable affects Pro immediately");
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            var feature = await db.FeatureDefinitions.SingleAsync(x => x.Key == "game.launch");
            Assert(feature.Version == 3 && feature.UpdatedAt == beforeGrant, "same-second changes still increment persistent feature version");
            var audits = await db.AuditEntries.Where(x => x.Action == "feature.configure").ToListAsync();
            var versions = audits.Select(x => (Before: JsonDocument.Parse(x.BeforeJson).RootElement.GetProperty("Value").GetProperty("Version").GetInt64(),
                After: JsonDocument.Parse(x.AfterJson).RootElement.GetProperty("Version").GetInt64())).OrderBy(x => x.Before).ToArray();
            Assert(versions.SequenceEqual(new[] { (1L, 2L), (2L, 3L) }), "feature audit records exact before and after versions");
        });
        await Task.WhenAll(Task.Run(() => Scope(provider => provider.GetRequiredService<AccountManagementService>().SetFeatureAsync("diagnostics.use", false, "standard", "concurrent feature A", "test-admin"))),
            Task.Run(() => Scope(provider => provider.GetRequiredService<AccountManagementService>().SetFeatureAsync("diagnostics.use", true, "pro", "concurrent feature B", "test-admin"))));
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Features.Single(x => x.Key == "diagnostics.use").Version == 3, "concurrent updates cannot lose feature version increments");
        clock.Advance(91 * 86400);
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Membership.Tier == "standard", "expired Pro automatically returns standard");

        login = await Accounts(a => a.LoginAsync(new(email, "second-passphrase", null)));
        await Accounts(a => a.RequestCodeAsync(new(email, "reset")));
        var reset = new ResetPasswordRequest(email, mailer.Code(email, "reset"), "third-passphrase");
        async Task<bool> Reset()
        {
            try { await Accounts(a => a.ResetPasswordAsync(reset)); return true; }
            catch (ApiException error) when (error.Error.Code == "invalid_code") { return false; }
        }
        Assert((await Task.WhenAll(Task.Run(Reset), Task.Run(Reset))).Count(x => x) == 1, "reset code consumed exactly once under concurrency");
        await Error(() => Accounts(a => a.RefreshAsync(new(login.RefreshToken))), 401, "invalid_session");
        await Accounts(a => a.LoginAsync(new(email, "third-passphrase", null)));
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            Assert(await db.MembershipChanges.CountAsync() == 2 && await db.AuditEntries.CountAsync() >= 6, "membership and admin mutations auditable");
            await DatabaseSetup.BackupAsync(db, Path.Combine(directory, "backup.sqlite"));
            Assert(File.Exists(Path.Combine(directory, "backup.sqlite")), "online SQLite backup completes");
        });
        await Scope(provider => DatabaseSetup.InitializeAsync(provider.GetRequiredService<AppDbContext>()));
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Email == email, "account survives reopening database");
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Features.Single(x => x.Key == "game.launch").Version == 3, "reinitialization preserves feature version and rules");
        assertions += await CompatibilityTests.RunAsync(services, clock, userId);
        assertions += await CommunityPresetTests.RunAsync(services, userId);
        await VerifyManualOrdersAsync(userId);
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            await db.DatabaseSchemas.ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, 999));
            try { await DatabaseSetup.InitializeAsync(db); }
            catch (InvalidOperationException) { Assert(true, "unknown schema rejected without silent upgrade"); return; }
            throw new Exception("FAILED: future schema should be rejected");
        });
        Console.WriteLine($"PASS: {assertions} account service assertions with real SQLite, concurrency, identity hashing, token rotation, backup and schema migration.");
    }

    private async Task VerifyManualOrdersAsync(string userId)
    {
        const string planId = "manual-test-plan";
        await Error(() => Scope(provider => provider.GetRequiredService<ManualOrderService>().CreateAsync(userId, planId)),
            503, "payments_unavailable");
        await Scope(provider => provider.GetRequiredService<AccountManagementService>().SetFeatureAsync(
            "game.launch", true, "pro", "test paid benefit", "test-admin"));
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = planId, Name = "测试会员", DurationDays = 30, PriceMinor = 1990, Enabled = true });
            await db.SaveChangesAsync();
        });
        var config = services.GetRequiredService<IConfiguration>();
        config["ManualPayment:Enabled"] = "true";
        config["ManualPayment:Instructions"] = "测试环境收款信息";
        await Scope(p=>{
            var details=JsonSerializer.SerializeToElement(p.GetRequiredService<ManualOrderService>().PaymentDetails);
            var channels=details.GetProperty("channels").EnumerateArray().ToArray();
            var resources=typeof(ManualOrderService).Assembly.GetManifestResourceNames();
            var expected=new[]{"wechat","alipay"}.Where(id=>resources.Contains($"Mu.Server.Payment.{id}.jpg")).ToArray();
            Assert(channels.Select(c=>c.GetProperty("id").GetString()).SequenceEqual(expected),"available private payment channels retain correct method IDs");
            foreach(var channel in channels){var url=channel.GetProperty("qrImageUrl").GetString()!;Assert(url.StartsWith("data:image/jpeg;base64,") && Convert.FromBase64String(url.Split(',')[1])[0]==0xff,"payment QR is the original JPEG image");}
            return Task.CompletedTask;
        });
        var order = await Scope(provider => provider.GetRequiredService<ManualOrderService>().CreateAsync(userId, planId));
        Assert(order.Status == "pending" && order.AmountMinor == 1990, "manual order snapshots amount without granting membership");
        Assert((await Scope(provider => provider.GetRequiredService<ManualOrderService>().CreateAsync(userId, planId))).Id == order.Id,
            "repeated purchase request reuses recent pending order");
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Membership.Tier == "standard", "pending order cannot unlock Pro");
        await Scope(provider => provider.GetRequiredService<ManualOrderService>().SubmitForReviewAsync(userId, order.Id));
        await Scope(provider => provider.GetRequiredService<ManualOrderService>().SubmitForReviewAsync(userId, order.Id));
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Membership.Tier == "standard", "self-reported payment cannot unlock Pro");
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            var plan = await db.Plans.SingleAsync(x => x.Id == planId);
            plan.PriceMinor = 9990;
            await db.SaveChangesAsync();
        });
        await Scope(provider => provider.GetRequiredService<ManualOrderService>().ConfirmAsync(
            order.Id, "receipt-1234", "independently verified receipt", "test-admin"));
        Assert((await Accounts(a => a.SnapshotAsync(userId))).Membership.Tier == "pro", "verified receipt grants Pro");
        var ordinary = await Accounts(a => a.SnapshotAsync(userId));
        Assert(ordinary.Membership.Kind == "advanced" && !ordinary.Features.Single(x => x.Key == "services.remote").Allowed,
            "ordinary advanced membership does not grant remote service");
        await Error(() => Scope(provider => provider.GetRequiredService<ManualOrderService>().ConfirmAsync(
            order.Id, "receipt-1234", "repeat confirmation", "test-admin")), 409, "order_not_pending");
        var second = await Scope(provider => provider.GetRequiredService<ManualOrderService>().CreateAsync(userId, planId));
        await Error(() => Scope(provider => provider.GetRequiredService<ManualOrderService>().ConfirmAsync(
            second.Id, "receipt-1234", "duplicate receipt", "test-admin")), 409, "reference_used");
        await Scope(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            Assert(await db.PaymentEvents.CountAsync() == 1 && await db.MembershipChanges.CountAsync(x => x.Source.StartsWith("manual-order:")) == 1,
                "repeat or reused receipt cannot create duplicate entitlement");
            Assert((await db.Orders.SingleAsync(x => x.Id == order.Id)).AmountMinor == 1990
                && (await db.Orders.SingleAsync(x => x.Id == second.Id)).Status == "pending", "plan edits and failed confirmation preserve orders");
        });
        var monthly = await Scope(p=>p.GetRequiredService<ManualOrderService>().CreateAsync(userId,"mu-pro-monthly"));
        Assert(monthly.AmountMinor==990 && monthly.DurationDays==30 && monthly.ConfigurationSessions==0,"monthly plan is 9.9 for 30 days");
        var assisted = await Scope(p=>p.GetRequiredService<ManualOrderService>().CreateAsync(userId,"mu-pro-assisted"));
        Assert(assisted.AmountMinor==2990 && assisted.DurationDays==30 && assisted.ConfigurationSessions==1,"29.9 includes 30 days and one configuration");
        Assert(!(await Accounts(a=>a.SnapshotAsync(userId))).Features.Single(x=>x.Key=="services.remote").Allowed,"unpaid Plus order does not grant service");
        await Error(()=>Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(assisted.Id,"test service complete","test-admin")),409,"service_not_pending");
        await Scope(async p=>{
            var db=p.GetRequiredService<AppDbContext>();
            var plan=await db.Plans.SingleAsync(x=>x.Id=="mu-pro-assisted");
            plan.DurationDays=90;plan.ConfigurationSessions=0;plan.PriceMinor=9990;
            await db.SaveChangesAsync();
        });
        await Scope(p=>p.GetRequiredService<ManualOrderService>().ConfirmAsync(assisted.Id,"receipt-assisted","verified assisted payment","test-admin"));
        var plus = await Accounts(a=>a.SnapshotAsync(userId));
        Assert(plus.Membership.Kind=="plus" && plus.Features.Single(x=>x.Key=="services.remote").Allowed,"paid Plus order grants remote entitlement");
        Assert(plus.Membership.PlusExpiresAt?.ToUnixTimeSeconds()==clock.GetUtcNow().ToUnixTimeSeconds()+30*86400L,"Plus duration uses immutable paid order, not edited plan or accumulated ordinary membership");
        await Scope(async p=>{
            var db=p.GetRequiredService<AppDbContext>();
            var saved=await db.Orders.SingleAsync(x=>x.Id==assisted.Id);
            Assert(saved.ConfigurationServiceStatus=="pending" && saved.ConfigurationSessions==1 && saved.DurationDays==30 && saved.AmountMinor==2990,"order retains original service, price and duration after plan edits");
        });
        await Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(assisted.Id,"configuration delivered","test-admin"));
        await Error(()=>Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(assisted.Id,"duplicate delivery","test-admin")),409,"service_not_pending");
        var lifetime = await Scope(p=>p.GetRequiredService<ManualOrderService>().CreateAsync(userId,"mu-pro-lifetime"));
        Assert(lifetime.AmountMinor==8880 && lifetime.DurationDays==0 && lifetime.GrantsMembership && lifetime.ConfigurationSessions==3,"88.8 lifetime includes permanent software access and three guidance sessions");
        await Scope(p=>p.GetRequiredService<ManualOrderService>().ConfirmAsync(lifetime.Id,"receipt-lifetime","verified lifetime payment","test-admin"));
        await Scope(p=>p.GetRequiredService<ManualOrderService>().ConfirmAsync(monthly.Id,"receipt-monthly","verified monthly payment","test-admin"));
        await Scope(async p=>{
            var db=p.GetRequiredService<AppDbContext>();
            Assert((await db.Users.SingleAsync(x=>x.Id==userId)).ProExpiresAt==MembershipPlans.LifetimeExpiry,"monthly purchase never shortens lifetime or overflows expiry");
            Assert((await db.Orders.SingleAsync(x=>x.Id==assisted.Id)).ConfigurationServiceStatus=="completed","service fulfillment persisted");
            await DatabaseSetup.InitializeAsync(db);
            Assert(await db.Plans.CountAsync(x=>x.Id.StartsWith("mu-pro-"))==3,"restart does not duplicate subscription catalogue");
        });
        clock.Advance(31*86400);
        var lifetimeOnly = await Accounts(a=>a.SnapshotAsync(userId));
        Assert(lifetimeOnly.Membership.Kind=="lifetime" && lifetimeOnly.Membership.Tier=="pro","Plus expiry preserves lifetime software membership");
        Assert(new[]{"games.supported.view","schemes.premium.configure","schemes.game.view"}.All(key=>lifetimeOnly.Features.Single(x=>x.Key==key).Allowed),"lifetime keeps all advanced software content after Plus expires");
        Assert(lifetimeOnly.Features.Single(x=>x.Key=="services.remote").Allowed && lifetimeOnly.Membership.RemoteSessionsRemaining==3,"lifetime retains its three guidance credits after Plus expires");
        for(var used=0;used<3;used++)
        {
            await Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(lifetime.Id,"guidance delivered","test-admin",used));
            await Error(()=>Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(lifetime.Id,"duplicate form submission","test-admin",used)),409,"service_not_pending");
            Assert((await Accounts(a=>a.SnapshotAsync(userId))).Membership.RemoteSessionsRemaining==2-used,"one completed guidance consumes exactly one credit");
        }
        await Error(()=>Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(lifetime.Id,"fourth guidance rejected","test-admin",3)),409,"service_not_pending");
        var exhausted=await Accounts(a=>a.SnapshotAsync(userId));
        Assert(exhausted.Membership.Tier=="pro"&&!exhausted.Features.Single(x=>x.Key=="services.remote").Allowed,"using all guidance leaves permanent software membership intact");
        await Scope(async p=>{
            var db=p.GetRequiredService<AppDbContext>();
            (await db.FeatureDefinitions.SingleAsync(x=>x.Key=="services.remote")).MinimumTier="standard";
            await db.SaveChangesAsync();
        });
        Assert(!(await Accounts(a=>a.SnapshotAsync(userId))).Features.Single(x=>x.Key=="services.remote").Allowed,"feature minimum-tier edit cannot bypass Plus-only requirement");

        var serviceUser=Guid.NewGuid().ToString("N");
        await Scope(async p=>{var db=p.GetRequiredService<AppDbContext>();db.Users.Add(new(){Id=serviceUser,UserName="guidance-only",Email="guidance-only@example.test"});await db.SaveChangesAsync();});
        var once=await Scope(p=>p.GetRequiredService<ManualOrderService>().CreateAsync(serviceUser,"mu-remote-once"));
        Assert(once.AmountMinor==3000&&!once.GrantsMembership&&once.DurationDays==0&&once.ConfigurationSessions==1,"30 yuan guidance order is service-only");
        Assert((await Accounts(a=>a.SnapshotAsync(serviceUser))).Membership.RemoteSessionsRemaining==0,"unpaid guidance order grants no credits");
        await Scope(p=>p.GetRequiredService<ManualOrderService>().ConfirmAsync(once.Id,"receipt-guidance-only","verified service payment","test-admin"));
        var serviceOnly=await Accounts(a=>a.SnapshotAsync(serviceUser));
        Assert(serviceOnly.Membership.Tier=="standard"&&serviceOnly.Membership.ExpiresAt==null&&serviceOnly.Membership.RemoteSessionsRemaining==1,"paid guidance never turns zero days into lifetime membership");
        Assert(serviceOnly.Features.Single(x=>x.Key=="services.remote").Allowed&&!serviceOnly.Features.Single(x=>x.Key=="schemes.premium.configure").Allowed,"service-only account can arrange guidance without unlocking software premium");
        await Scope(async p=>Assert(!await p.GetRequiredService<AppDbContext>().MembershipChanges.AnyAsync(x=>x.UserId==serviceUser),"service payment creates no membership-change record"));
        await Scope(p=>p.GetRequiredService<ManualOrderService>().CompleteConfigurationAsync(once.Id,"single guidance delivered","test-admin"));
        Assert(!(await Accounts(a=>a.SnapshotAsync(serviceUser))).Features.Single(x=>x.Key=="services.remote").Allowed,"service-only contact access expires when the credit is consumed");

    }

    private async Task VerifyVersionOneUpgradeAsync()
    {
        var assembly = typeof(Suite).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith("schema-v1.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var fixture = await reader.ReadToEndAsync();
        var path = Path.Combine(directory, "legacy-v1.sqlite");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Default Timeout=20").Options;
        await using (var db = new AppDbContext(options))
        {
            await ExecuteFixtureAsync(db, fixture);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Plans VALUES ('legacy-plan','历史套餐',60,1990,'CNY',1);");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Orders (Id,UserId,PlanId,PlanName,DurationDays,Status,AmountMinor,Currency,CreatedAt,Provider) VALUES ('legacy-order','legacy-user','legacy-plan','历史套餐',60,'pending',1990,'CNY',1700000000,'manual');");
            Assert(await Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('FeatureDefinitions') WHERE name='Version';") == 0, "frozen v1 fixture physically has no feature Version column");
            Assert(await Scalar(db, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 1, "fixture starts at deployed schema one");
            await DatabaseSetup.InitializeAsync(db);
            Assert(!(await db.Plans.SingleAsync(x=>x.Id=="legacy-plan")).Enabled,"catalogue upgrade retires old sales plans");
            var legacyOrder=await db.Orders.SingleAsync(x=>x.Id=="legacy-order");
            Assert(legacyOrder.DurationDays==60 && legacyOrder.AmountMinor==1990 && legacyOrder.ConfigurationSessions==0 && legacyOrder.Status=="pending","catalogue upgrade preserves existing orders and entitlements");
            var legacy = await db.Users.AsNoTracking().SingleAsync();
            Assert(legacy.Id == "legacy-user" && legacy.Email == "legacy@example.com" && legacy.ProExpiresAt == 1800000000
                && legacy.PasswordHash == "fixture-password-hash" && legacy.SecurityStamp == "fixture-security-stamp", "upgrade preserves historical user, membership and credentials");
            var rule = await db.FeatureDefinitions.AsNoTracking().SingleAsync(x => x.Key == "library.manage");
            Assert(!rule.Enabled && rule.MinimumTier == "pro" && rule.UpdatedAt == 1700000001 && rule.Version == 1, "upgrade preserves operator rule and initializes its version");
            var audit = await db.AuditEntries.AsNoTracking().SingleAsync(x=>x.Id=="legacy-audit");
            Assert(audit.Id == "legacy-audit" && audit.BeforeJson == "{\"legacy\":true}" && audit.AfterJson == "{\"enabled\":false}", "upgrade preserves historical audit payloads verbatim");
            Assert(await db.AuthSessions.CountAsync() == 1 && await db.MembershipChanges.CountAsync() == 1, "upgrade preserves sessions and membership history");
            Assert(await db.FeatureDefinitions.CountAsync() == AccountService.InitialFeatures.Length && await db.FeatureDefinitions.AllAsync(x => x.Version == 1), "newly seeded and migrated features both start at version one");
            Assert(await Scalar(db, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == DatabaseSetup.CurrentVersion, "successful migration advances schema marker to four");
            Assert(await db.CompatibilityGames.CountAsync() >= 4 && !await db.CompatibilityTests.AnyAsync(), "upgrade seeds catalog without fabricated tests");
        }
        await using (var reopened = new AppDbContext(options))
        {
            await DatabaseSetup.InitializeAsync(reopened);
            await DatabaseSetup.InitializeAsync(reopened);
            Assert(await Scalar(reopened, "SELECT COUNT(*) FROM pragma_table_info('FeatureDefinitions') WHERE name='Version';") == 1,
                "repeated initialization does not repeat ALTER or duplicate column");
            Assert(await reopened.Users.CountAsync() == 1 && await reopened.AuditEntries.CountAsync() == 2 && !(await reopened.FeatureDefinitions.SingleAsync(x => x.Key == "library.manage")).Enabled,
                "reopening migrated database preserves users, audit and custom rules");
        }

        var failingOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "legacy-failing.sqlite")};Default Timeout=20").Options;
        await using (var failing = new AppDbContext(failingOptions))
        {
            await ExecuteFixtureAsync(failing, fixture);
            await failing.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_schema_update BEFORE UPDATE ON DatabaseSchemas BEGIN SELECT RAISE(ABORT, 'injected migration failure'); END;");
            try { await DatabaseSetup.InitializeAsync(failing); throw new Exception("FAILED: migration failure expected"); }
            catch (DbUpdateException) { assertions++; }
        }
        await using (var check = new AppDbContext(failingOptions))
        {
            Assert(await Scalar(check, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 1, "failed migration leaves schema marker at one");
            Assert(await Scalar(check, "SELECT COUNT(*) FROM pragma_table_info('FeatureDefinitions') WHERE name='Version';") == 0, "failed marker update also rolls back successful ALTER");
            Assert(await Scalar(check, "SELECT COUNT(*) FROM sqlite_master WHERE name='CompatibilityTests';") == 0, "failed v1 upgrade rolls back compatibility tables");
            Assert(await check.Users.CountAsync() == 1 && await check.AuditEntries.CountAsync() == 1
                && await Scalar(check, "SELECT COUNT(*) FROM FeatureDefinitions WHERE Key='library.manage' AND Enabled=0 AND MinimumTier='pro';") == 1,
                "failed migration preserves historical data and rules");
            await check.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_schema_update;");
            await DatabaseSetup.InitializeAsync(check);
            Assert(await Scalar(check, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == DatabaseSetup.CurrentVersion, "migration can retry cleanly after failure is removed");
        }

        var versionTwo = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "legacy-v2.sqlite")}").Options;
        await using (var db = new AppDbContext(versionTwo))
        {
            await ExecuteFixtureAsync(db, fixture);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE FeatureDefinitions ADD COLUMN Version INTEGER NOT NULL DEFAULT 1; UPDATE FeatureDefinitions SET Version=7; UPDATE DatabaseSchemas SET Version=2;");
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_schema_update BEFORE UPDATE ON DatabaseSchemas BEGIN SELECT RAISE(ABORT, 'injected migration failure'); END;");
            try { await DatabaseSetup.InitializeAsync(db); throw new Exception("FAILED: v2 migration failure expected"); }
            catch (DbUpdateException) { assertions++; }
        }
        await using (var db = new AppDbContext(versionTwo))
        {
            Assert(await Scalar(db, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 2, "failed v2 upgrade preserves marker");
            Assert(await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='CompatibilityGames';") == 0, "failed v2 upgrade rolls back catalog table");
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_schema_update;");
            await DatabaseSetup.InitializeAsync(db);
            Assert((await db.DatabaseSchemas.SingleAsync()).Version == DatabaseSetup.CurrentVersion && await db.CompatibilityGames.CountAsync() >= 4 && !await db.CompatibilityTests.AnyAsync() && !await db.GameTelemetry.AnyAsync(), "v2 retry creates v4 catalog with no reports");
            var user = await db.Users.SingleAsync();
            Assert(user.Id == "legacy-user" && user.PasswordHash == "fixture-password-hash" && await db.AuthSessions.CountAsync() == 1 && await db.AuditEntries.CountAsync(x=>x.Id=="legacy-audit") == 1, "v2 upgrade preserves user credentials, sessions and audit");
            Assert((await db.FeatureDefinitions.SingleAsync(x => x.Key == "library.manage")).Version == 7, "v2 upgrade preserves feature configuration version");
            var catalogCount = await db.CompatibilityGames.CountAsync();
            await DatabaseSetup.InitializeAsync(db);
            Assert(await db.CompatibilityGames.CountAsync() == catalogCount, "v3 reinitialization does not duplicate catalog");
        }
    }

    private static async Task<long> Scalar(AppDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task ExecuteFixtureAsync(AppDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

sealed class TestMailer : IVerificationEmailSender
{
    private readonly ConcurrentDictionary<string, string> codes = new();
    public bool Fail { get; set; }
    public string Code(string email, string purpose) => codes[$"{email}:{purpose}"];
    public Task SendAsync(string email, string code, string purpose, string requestId, CancellationToken cancellationToken = default)
    {
        if (Fail) throw new ApiException(503, "email_unavailable", "simulated delivery failure");
        codes[$"{email}:{purpose}"] = code;
        return Task.CompletedTask;
    }
}

sealed class FakeClock : TimeProvider
{
    private long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Interlocked.Read(ref seconds));
    public void Advance(long value) => Interlocked.Add(ref seconds, value);
}
