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
        await VerifyVersionOneUpgradeAsync();
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
        Assert(session.Account.Membership.Tier == "standard" && session.Account.Features.Count == 9 && session.Account.Features.All(x => x.Allowed), "new account has standard initial permissions");
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
            Assert(await Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('FeatureDefinitions') WHERE name='Version';") == 0, "frozen v1 fixture physically has no feature Version column");
            Assert(await Scalar(db, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 1, "fixture starts at deployed schema one");
            await DatabaseSetup.InitializeAsync(db);
            var legacy = await db.Users.AsNoTracking().SingleAsync();
            Assert(legacy.Id == "legacy-user" && legacy.Email == "legacy@example.com" && legacy.ProExpiresAt == 1800000000
                && legacy.PasswordHash == "fixture-password-hash" && legacy.SecurityStamp == "fixture-security-stamp", "upgrade preserves historical user, membership and credentials");
            var rule = await db.FeatureDefinitions.AsNoTracking().SingleAsync(x => x.Key == "library.manage");
            Assert(!rule.Enabled && rule.MinimumTier == "pro" && rule.UpdatedAt == 1700000001 && rule.Version == 1, "upgrade preserves operator rule and initializes its version");
            var audit = await db.AuditEntries.AsNoTracking().SingleAsync();
            Assert(audit.Id == "legacy-audit" && audit.BeforeJson == "{\"legacy\":true}" && audit.AfterJson == "{\"enabled\":false}", "upgrade preserves historical audit payloads verbatim");
            Assert(await db.AuthSessions.CountAsync() == 1 && await db.MembershipChanges.CountAsync() == 1, "upgrade preserves sessions and membership history");
            Assert(await db.FeatureDefinitions.CountAsync() == AccountService.InitialFeatures.Length && await db.FeatureDefinitions.AllAsync(x => x.Version == 1), "newly seeded and migrated features both start at version one");
            Assert(await Scalar(db, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 2, "successful migration advances schema marker to two");
        }
        await using (var reopened = new AppDbContext(options))
        {
            await DatabaseSetup.InitializeAsync(reopened);
            await DatabaseSetup.InitializeAsync(reopened);
            Assert(await Scalar(reopened, "SELECT COUNT(*) FROM pragma_table_info('FeatureDefinitions') WHERE name='Version';") == 1,
                "repeated initialization does not repeat ALTER or duplicate column");
            Assert(await reopened.Users.CountAsync() == 1 && await reopened.AuditEntries.CountAsync() == 1 && !(await reopened.FeatureDefinitions.SingleAsync(x => x.Key == "library.manage")).Enabled,
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
            Assert(await check.Users.CountAsync() == 1 && await check.AuditEntries.CountAsync() == 1
                && await Scalar(check, "SELECT COUNT(*) FROM FeatureDefinitions WHERE Key='library.manage' AND Enabled=0 AND MinimumTier='pro';") == 1,
                "failed migration preserves historical data and rules");
            await check.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_schema_update;");
            await DatabaseSetup.InitializeAsync(check);
            Assert(await Scalar(check, "SELECT Version FROM DatabaseSchemas WHERE Id=1;") == 2, "migration can retry cleanly after failure is removed");
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
