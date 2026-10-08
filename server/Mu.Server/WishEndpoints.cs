using System.Security.Claims;
using Mu.Server.Services;
using Mu.Wishes;

namespace Mu.Server;

public static class WishEndpoints
{
    public static void MapWishApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/wishes").RequireAuthorization("Client");
        api.MapGet("/", async (string? filter, string? q, string? sort, int? page, ClaimsPrincipal user, WishService wishes, CancellationToken ct) =>
            Results.Ok(await wishes.ListAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, filter, q, sort, page ?? 1, ct)));
        api.MapGet("/{id}", async (string id, ClaimsPrincipal user, WishService wishes, CancellationToken ct) =>
            Results.Ok(await wishes.DetailAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct)));
        api.MapPost("/", async (WishSubmission input, ClaimsPrincipal user, WishService wishes, CancellationToken ct) =>
            Results.Ok(await wishes.SubmitAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, input, ct)));
        api.MapPut("/{id}/vote", async (string id, WishVoteRequest input, ClaimsPrincipal user, WishService wishes, CancellationToken ct) =>
            Results.Ok(await wishes.VoteAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, input.Voted, ct)));
        api.MapPost("/{id}/comments", async (string id, WishCommentRequest input, ClaimsPrincipal user, WishService wishes, CancellationToken ct) =>
            Results.Ok(await wishes.CommentAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, input, ct)));
    }
}
