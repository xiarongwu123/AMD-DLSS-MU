using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server.Security;

public sealed class ClientBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AppDbContext db, TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MuClientBearer";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..].Trim();
        if (token.Length is < 32 or > 256) return AuthenticateResult.Fail("Invalid token");
        var hash = AccountService.HashToken(token);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var session = await db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(x => x.AccessHash == hash, Context.RequestAborted);
        if (session == null || session.RevokedAt != null || session.AccessExpiresAt <= now) return AuthenticateResult.Fail("Session expired");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.UserId, Context.RequestAborted);
        if (user == null) return AuthenticateResult.Fail("Unknown user");
        if (user.DisabledAt != null)
        {
            Context.Items["Mu.AccountDisabled"] = true;
            return AuthenticateResult.Fail("Account disabled");
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Email ?? ""), new Claim("sid", session.Id), new Claim("family", session.FamilyId)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var disabled = Context.Items.ContainsKey("Mu.AccountDisabled");
        Response.StatusCode = disabled ? 403 : 401;
        return Response.WriteAsJsonAsync(new ApiError(disabled ? "account_disabled" : "invalid_session",
            disabled ? "账号已被停用。" : "登录已失效，请重新登录。"));
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Response.WriteAsJsonAsync(new ApiError("forbidden", "没有权限执行此操作。"));
    }
}
