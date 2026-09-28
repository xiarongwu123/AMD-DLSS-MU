using System.Security.Claims;
using Mu.Compatibility;
using Mu.Server.Services;

namespace Mu.Server;

public static class CompatibilityEndpoints
{
    public static void MapCompatibilityApi(this WebApplication app)
    {
        var publicApi = app.MapGroup("/api/v1/compatibility/public").RequireRateLimiting("compatibility-public");
        publicApi.MapGet("/games", async (string? q, string? gpu, int? page, int? pageSize, CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(await compatibility.PublicSearchAsync(q, gpu, page ?? 1, pageSize ?? 50, ct)));
        publicApi.MapGet("/games/{id}", async (string id, string? gpu, CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(await compatibility.PublicDetailAsync(id, gpu, ct)));
        publicApi.MapGet("/gpus", async (CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(new { items = await compatibility.PublicGpusAsync(ct) }));

        var api = app.MapGroup("/api/v1/compatibility").RequireAuthorization("Client");
        api.MapGet("/games", async (string? q, string? gpu, int? page, int? pageSize, ClaimsPrincipal user, CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(await compatibility.SearchAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, q, gpu, page ?? 1, pageSize ?? 50, ct)));
        api.MapGet("/games/{id}", async (string id, string? gpu, ClaimsPrincipal user, CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(await compatibility.DetailAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, gpu, ct)));
        api.MapPost("/tests", async (CompatibilitySubmission input, ClaimsPrincipal user, CompatibilityService compatibility, CancellationToken ct) =>
            Results.Ok(await compatibility.SubmitAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!, input, ct)));
    }
}
