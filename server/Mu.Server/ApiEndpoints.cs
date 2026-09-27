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
            var reason = feature == null ? "unknown_feature" : !feature.Enabled ? "feature_disabled" : !allowed ? "pro_required" : "allowed";
            return Results.Json(new { allowed, reason, account, code = allowed ? null : reason,
                message = allowed ? null : reason == "pro_required" ? "此功能需要有效的 Pro 会员。" : "该功能暂不可用。" }, statusCode: allowed ? 200 : 403);
        });
        api.MapGet("/plans", async (AppDbContext db) => Results.Ok(new
        {
            items = await db.Plans.AsNoTracking().Where(x => x.Enabled).Select(x => new { x.Id, x.Name, x.DurationDays, x.PriceMinor, x.Currency }).ToArrayAsync(),
            paymentsEnabled = false
        }));
        authenticated.MapPost("/orders", async (CreateOrderRequest input, ClaimsPrincipal user, IPaymentProvider payments, CancellationToken ct) =>
        {
            // The only registered provider is disabled. No order or membership is mutated before a real provider exists.
            return Results.Ok(await payments.CreateCheckoutAsync(new Order { UserId = UserId(user), PlanId = input.PlanId }, ct));
        });
        authenticated.MapGet("/orders", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var id = UserId(user);
            return Results.Ok(new { items = await db.Orders.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Id, x.PlanId, x.PlanName, x.DurationDays, x.Status, x.AmountMinor, x.Currency, x.CreatedAt, x.PaidAt }).ToArrayAsync(), paymentsEnabled = false });
        });
        authenticated.MapGet("/orders/{orderId}", async (string orderId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var id = UserId(user);
            var order = await db.Orders.AsNoTracking().Where(x => x.Id == orderId && x.UserId == id)
                .Select(x => new { x.Id, x.PlanId, x.PlanName, x.DurationDays, x.Status, x.AmountMinor, x.Currency, x.CreatedAt, x.PaidAt }).SingleOrDefaultAsync();
            return order == null ? Results.NotFound(new ApiError("order_not_found", "订单不存在。")) : Results.Ok(order);
        });
    }
}
