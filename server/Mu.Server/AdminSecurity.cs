using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Mu.Server.Data;

namespace Mu.Server;

public static class AdminSecurity
{
    public const string Scheme = "MuAdmin";
    public const string PendingScheme = "MuAdminMfa";
    public const string Role = "Admin";
    private const string StampClaim = "mu:security_stamp";

    public static IServiceCollection AddMuAdmin(this IServiceCollection services)
    {
        services.AddSingleton<AdminLoginThrottle>();
        services.AddAuthentication()
            .AddCookie(Scheme, options => ConfigureCookie(options, Scheme, "/admin/login", TimeSpan.FromHours(2)))
            .AddCookie(PendingScheme, options => ConfigureCookie(options, PendingScheme, "/admin/login", TimeSpan.FromMinutes(5)));
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Scheme, policy => policy.AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser().RequireRole(Role).RequireClaim("amr", "mfa"));
            options.AddPolicy(PendingScheme, policy => policy.AddAuthenticationSchemes(PendingScheme)
                .RequireAuthenticatedUser().RequireRole(Role));
        });
        return services;
    }

    public static WebApplication UseMuAdmin(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/admin") || context.Request.Path.StartsWithSegments("/legal"))
            {
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'none'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
                context.Response.Headers.XFrameOptions = "DENY";
                context.Response.Headers["Referrer-Policy"] = "same-origin";
                if (context.Request.Path.StartsWithSegments("/admin")) context.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });
        app.UseStaticFiles();
        return app;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, string scheme, string login, TimeSpan duration)
    {
        options.Cookie.Name = scheme == Scheme ? "__Host-MuAdmin" : "__Host-MuAdminMfa";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.LoginPath = login;
        options.AccessDeniedPath = login;
        options.ExpireTimeSpan = duration;
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
            if (user is null || user.DisabledAt is not null || !await users.IsInRoleAsync(user, Role)
                || !string.Equals(context.Principal?.FindFirstValue(StampClaim), await users.GetSecurityStampAsync(user), StringComparison.Ordinal)
                || (scheme == Scheme && !await users.GetTwoFactorEnabledAsync(user)))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(scheme);
            }
        };
    }

    public static async Task SignInAsync(HttpContext context, UserManager<ApplicationUser> users, ApplicationUser user, bool verified)
    {
        var scheme = verified ? Scheme : PendingScheme;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? user.Id),
            new(ClaimTypes.Role, Role),
            new(StampClaim, await users.GetSecurityStampAsync(user))
        };
        if (verified) claims.Add(new Claim("amr", "mfa"));
        await context.SignInAsync(scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        if (verified) await context.SignOutAsync(PendingScheme);
    }

    public static string NormalizeCode(string? value) => (value ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
}

public sealed class AdminLoginThrottle
{
    private sealed record Attempt(int Count, DateTimeOffset ResetAt);
    private readonly ConcurrentDictionary<string, Attempt> attempts = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> usedCodes = new();

    public bool Allow(string category, string identifier, int limit)
    {
        var now = DateTimeOffset.UtcNow;
        if (attempts.Count > 10000)
            foreach (var entry in attempts.Where(pair => pair.Value.ResetAt <= now)) attempts.TryRemove(entry.Key, out _);
        var key = category + ":" + identifier;
        if (attempts.Count >= 20000 && !attempts.ContainsKey(key)) return false;
        var attempt = attempts.AddOrUpdate(key, _ => new Attempt(1, now.AddMinutes(10)),
            (_, current) => current.ResetAt <= now ? new Attempt(1, now.AddMinutes(10)) : current with { Count = current.Count + 1 });
        return attempt.Count <= limit;
    }

    public bool ConsumeCode(string userId, string code)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in usedCodes.Where(pair => pair.Value <= now)) usedCodes.TryRemove(entry.Key, out _);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + code)));
        return usedCodes.TryAdd(key, now.AddMinutes(3));
    }
}
