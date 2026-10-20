using System.Security.Claims;
using Mu.Presets;
using Mu.Server.Services;

namespace Mu.Server;

public static class CommunityPresetEndpoints
{
    public static void MapCommunityPresets(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/presets").RequireAuthorization("Client");
        // Both entry points always list every author's games and universal shares.
        // Ignore legacy gameKey query parameters; only publishing/applying needs a game.
        api.MapGet("/", async (int? page, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, ct, page ?? 1)));
        api.MapGet("/all", async (int? page, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, ct, page ?? 1)));
        api.MapGet("/{id}", async (string id, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.DetailAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct)));
        api.MapGet("/{id}/attachment", async (string id, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.AttachmentAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct)));
        api.MapPost("/", async (PresetSubmission input, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.SubmitAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, input, ct)));
    }
}
