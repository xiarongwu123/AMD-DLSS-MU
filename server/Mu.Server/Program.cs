using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Security;
using Mu.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
var databasePath = Path.GetFullPath(builder.Configuration["Database:Path"] ?? "data/accounts.sqlite");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var keyPath = Path.GetFullPath(builder.Configuration["DataProtection:Path"] ?? "data/keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection().SetApplicationName("Mu.Accounts").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(new SqliteConnectionStringBuilder
{
    DataSource = databasePath, ForeignKeys = true, DefaultTimeout = 5
}.ToString()));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 1;
    options.Password.RequireDigit = options.Password.RequireLowercase = options.Password.RequireUppercase = options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountManagementService>();
builder.Services.AddSingleton<IPaymentProvider, DisabledPaymentProvider>();
builder.Services.AddHttpClient<IVerificationEmailSender, ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ClientBearerHandler>(ClientBearerHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy("Client", policy => policy.AddAuthenticationSchemes(ClientBearerHandler.SchemeName).RequireAuthenticatedUser()));
builder.Services.AddRazorPages();
builder.Services.AddMuAdmin();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var value in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(value, out var address)) throw new InvalidOperationException("Invalid Proxy:KnownProxies entry.");
        options.KnownProxies.Add(address);
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 40, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.AddPolicy("email", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiError("rate_limited", "请求过于频繁，请稍后重试。", seconds), ct);
    };
});
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseSetup.InitializeAsync(db);
    var backup = Array.IndexOf(args, "--backup");
    if (backup >= 0)
    {
        if (backup + 1 >= args.Length) throw new ArgumentException("Usage: --backup /absolute/path/accounts.sqlite");
        await DatabaseSetup.BackupAsync(db, args[backup + 1]);
        Console.WriteLine("Account database backup completed and integrity checked.");
        return;
    }
}
if (await app.RunAdminCommandAsync(args)) return;
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && Encoding.UTF8.GetByteCount(builder.Configuration["Auth:CodePepper"] ?? "") < 32)
    throw new InvalidOperationException("Auth:CodePepper must contain at least 32 bytes. Supply it through a server secret environment file.");

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && !context.Request.IsHttps && context.Request.Path != "/health" && context.Request.Path != "/api/health")
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new ApiError("https_required", "请使用 HTTPS 访问。"));
        return;
    }
    try { await next(); }
    catch (ApiException error)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = error.StatusCode;
        if (error.Error.RetryAfterSeconds is int seconds) context.Response.Headers.RetryAfter = seconds.ToString();
        await context.Response.WriteAsJsonAsync(error.Error);
    }
    catch (Exception error) when (error is JsonException or BadHttpRequestException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = error is BadHttpRequestException request ? request.StatusCode : 400;
        await context.Response.WriteAsJsonAsync(new ApiError("invalid_request", "请求格式无效。"));
    }
    catch (Exception error)
    {
        if (context.Response.HasStarted) throw;
        app.Logger.LogError(error, "Account request failed at {Path}", context.Request.Path);
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new ApiError("service_unavailable", "服务暂时不可用，请稍后重试。"));
    }
});
app.UseMuAdmin();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/admin"));
app.MapGet("/health", async (AppDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "ok", service = "mu-accounts" }) : Results.StatusCode(503));
app.MapGet("/api/health", async (AppDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "ok", service = "mu-accounts" }) : Results.StatusCode(503));
app.MapAccountApi();
app.MapRazorPages();
await app.RunAsync();

public partial class Program { }
