using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Server.Services;

namespace Mu.Server;

public static class ApiEndpoints
{
    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public static void MapAccountApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/render-rules", async (string gameId, int? format, NeuralRuleStore rules, HttpContext context, ClaimsPrincipal user, AccountService accounts, AppDbContext db) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(gameId, "^[a-z0-9:_-]{1,100}$")) return Results.BadRequest();
            context.Response.Headers.CacheControl = "no-store";
            var account = await accounts.SnapshotAsync(UserId(user));
            if (account.Membership.Tier != "pro" || !account.Features.Any(f => f.Key == "schemes.game.view" && f.Allowed))
                return Results.Json(new { code = "pro_required", message = "游戏专属推荐需要有效的高级会员。", account }, statusCode: 403);
            try
            {
                var document = rules.Read();
                if(format==2)return Results.Ok(await RecommendationEvidence.AttachAsync(document with {Rules=document.Rules.Where(r=>r.GameId==gameId).ToArray()},gameId,db,DateTimeOffset.UtcNow));
                // Keep strict older clients compatible: no new fields on the legacy response.
                return Results.Ok(new {document.SchemaVersion,document.Revision,document.ExpiresUtc,Rules=document.Rules.Where(r=>r.GameId==gameId).ToArray()});
            }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
            { return Results.Json(new { message = "兼容规则暂不可用。" }, statusCode: 503); }
        }).RequireAuthorization("Client");
        api.MapPost("/auth/code", async (CodeRequest input, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.RequestCodeAsync(input, ct);
            return Results.Ok(new { message = "如该邮箱符合操作条件，验证码已发送，请检查邮箱。", retryAfterSeconds = 60 });
        }).RequireRateLimiting("email");
        api.MapPost("/auth/register", async (RegisterRequest input, AccountService accounts) => Results.Ok(await accounts.RegisterAsync(input))).RequireRateLimiting("auth");
        api.MapPost("/auth/login", async (LoginRequest input, AccountService accounts) => Results.Ok(await accounts.LoginAsync(input))).RequireRateLimiting("auth");
        api.MapPost("/auth/refresh", async (RefreshRequest input, AccountService accounts) => Results.Ok(await accounts.RefreshAsync(input))).RequireRateLimiting("auth");
        api.MapPost("/auth/reset-password", async (ResetPasswordRequest input, AccountService accounts) =>
        {
            await accounts.ResetPasswordAsync(input);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        var authenticated = api.MapGroup("").RequireAuthorization("Client");
        authenticated.MapPost("/account/redeem", async (RedeemMembershipRequest input, ClaimsPrincipal user, MembershipCodeService codes, AccountService accounts) =>
        {
            await codes.RedeemAsync(UserId(user), input.Code, input.PlanId);
            return Results.Ok(await accounts.SnapshotAsync(UserId(user)));
        }).RequireRateLimiting("auth");
        authenticated.MapGet("/account/me", async (ClaimsPrincipal user, AccountService accounts) => Results.Ok(await accounts.SnapshotAsync(UserId(user))));
        authenticated.MapPost("/auth/logout", async (ClaimsPrincipal user, AccountService accounts) =>
        {
            await accounts.RevokeFamilyAsync(user.FindFirstValue("family")!);
            return Results.NoContent();
        });
        authenticated.MapPost("/account/password", async (ChangePasswordRequest input, ClaimsPrincipal user, AccountService accounts) =>
        {
            await accounts.ChangePasswordAsync(UserId(user), input);
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        authenticated.MapPost("/account/authorize", async (AuthorizeRequest input, ClaimsPrincipal user, AccountService accounts) =>
        {
            var account = await accounts.SnapshotAsync(UserId(user));
            var feature = account.Features.SingleOrDefault(x => x.Key == input.FeatureKey);
            var allowed = feature?.Allowed == true;
            var reason = feature == null ? "unknown_feature" : !feature.Enabled ? "feature_disabled" : !allowed ? input.FeatureKey == "services.remote" ? "remote_service_required" : "pro_required" : "allowed";
            return Results.Json(new { allowed, reason, account, code = allowed ? null : reason,
                message = allowed ? null : reason == "remote_service_required" ? "需要有效的 Plus 权益或剩余远程指导次数。" : reason == "pro_required" ? "此功能需要有效的 Pro 会员。" : "该功能暂不可用。" }, statusCode: allowed ? 200 : 403);
        });
        authenticated.MapGet("/catalog/supported-games", async (ClaimsPrincipal user, AccountService accounts, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var account = await accounts.SnapshotAsync(UserId(user));
            if (account.Membership.Tier != "pro")
                return Results.Json(new { code = "pro_required", message = "支持游戏库需要有效的高级会员。", account }, statusCode: 403);
            if (!account.Features.Any(f => f.Key == "games.supported.view" && f.Allowed))
                return Results.Json(new { code = "feature_disabled", message = "支持游戏库暂不可用。", account }, statusCode: 403);
            using var stream = typeof(ApiEndpoints).Assembly.GetManifestResourceStream("Mu.Server.SupportedGames")
                ?? throw new ApiException(503, "catalog_unavailable", "支持游戏库暂未配置，请联系管理员。");
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
            return Results.Ok(new { catalog = document.RootElement.Clone(), account });
        });
        api.MapGet("/plans", async (AppDbContext db, ManualOrderService orders) => Results.Ok(new
        {
            items = await db.Plans.AsNoTracking().Where(x => x.Enabled).OrderBy(x=>x.Id=="mu-pro-monthly" ? 0 : x.Id=="mu-pro-lifetime" ? 1 : x.Id=="mu-pro-assisted" ? 2 : 3).ThenBy(x=>x.PriceMinor).Select(x => new { x.Id, x.Name, x.DurationDays, x.GrantsMembership, x.ConfigurationSessions, x.PriceMinor, x.Currency }).ToArrayAsync(),
            paymentsEnabled = await orders.AvailableAsync(), paymentMethod = "cdk", cdkPlanMatching = true,
            payment = await orders.AvailableAsync() ? orders.PaymentDetails : null
        }));
        authenticated.MapPost("/orders", async (CreateOrderRequest input, ClaimsPrincipal user, ManualOrderService orders, CancellationToken ct) =>
        {
            var order = await orders.CreateAsync(UserId(user), input.PlanId, ct);
            return Results.Ok(new { order.Id, order.PlanId, order.PlanName, order.DurationDays, order.GrantsMembership, order.ConfigurationSessions, order.ConfigurationSessionsUsed, order.ConfigurationServiceStatus, order.Status,
                order.AmountMinor, order.Currency, order.CreatedAt, payment = orders.PaymentDetails });
        });
        authenticated.MapPost("/orders/{orderId}/review", async (string orderId, ClaimsPrincipal user, ManualOrderService orders, CancellationToken ct) =>
        {
            await orders.SubmitForReviewAsync(UserId(user), orderId, ct);
            return Results.NoContent();
        });
        authenticated.MapGet("/orders", async (ClaimsPrincipal user, AppDbContext db, ManualOrderService orders) =>
        {
            var id = UserId(user);
            return Results.Ok(new { items = await db.Orders.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Id, x.PlanId, x.PlanName, x.DurationDays, x.GrantsMembership, x.ConfigurationSessions, x.ConfigurationSessionsUsed, x.ConfigurationServiceStatus, x.Status, x.AmountMinor, x.Currency, x.CreatedAt, x.PaidAt }).ToArrayAsync(), paymentsEnabled = await orders.AvailableAsync() });
        });
        authenticated.MapGet("/orders/{orderId}", async (string orderId, ClaimsPrincipal user, AppDbContext db, ManualOrderService orders) =>
        {
            var id = UserId(user);
            var order = await db.Orders.AsNoTracking().Where(x => x.Id == orderId && x.UserId == id)
                .Select(x => new { x.Id, x.PlanId, x.PlanName, x.DurationDays, x.GrantsMembership, x.ConfigurationSessions, x.ConfigurationSessionsUsed, x.ConfigurationServiceStatus, x.Status, x.AmountMinor, x.Currency, x.CreatedAt, x.PaidAt }).SingleOrDefaultAsync();
            return order == null ? Results.NotFound(new ApiError("order_not_found", "订单不存在。"))
                : Results.Ok(new { order.Id, order.PlanId, order.PlanName, order.DurationDays, order.GrantsMembership, order.ConfigurationSessions, order.ConfigurationSessionsUsed, order.ConfigurationServiceStatus, order.Status,
                    order.AmountMinor, order.Currency, order.CreatedAt, order.PaidAt,
                    payment = (order.Status is "pending" or "review_requested") && await orders.AvailableAsync() ? orders.PaymentDetails : null });
        });
    }
}
