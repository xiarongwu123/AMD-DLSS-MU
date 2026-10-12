using System.Security.Claims;
using Mu.Presets;
using Mu.Server.Services;

namespace Mu.Server;

public static class CommunityPresetEndpoints
{
    public static void MapCommunityPresets(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/presets").RequireAuthorization("Client");
        api.MapGet("/", async (string? gameKey, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, gameKey, ct)));
        api.MapGet("/{id}", async (string id, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.DetailAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct)));
        api.MapPost("/", async (PresetSubmission input, ClaimsPrincipal user, CommunityPresetService service, CancellationToken ct) =>
            Results.Ok(await service.SubmitAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, input, ct)));
    }
}
