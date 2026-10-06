using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;

namespace Mu.Server.Pages.Admin;

public sealed class TelemetryModel(AppDbContext db) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;
    [BindProperty(SupportsGet = true)] public string? Game { get; set; }

    public int SessionCount { get; private set; }
    public int AccountCount { get; private set; }
    public int FpsSessionCount { get; private set; }
    public int InsufficientSessionCount { get; private set; }
    public long? LatestEndedAt { get; private set; }
    public List<GameSummary> Games { get; private set; } = [];
    public List<GpuSummary> Gpus { get; private set; } = [];
    public List<ModeSummary> Modes { get; private set; } = [];
    public List<RecentSession> Recent { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Days is not (0 or 7 or 30 or 90)) Days = 30;
        Game = Game?.Trim();
        if (Game?.Length > 160) Game = Game[..160];
        if (Game == "") Game = null;

        var query = db.GameTelemetry.AsNoTracking();
        if (Days != 0)
        {
            var from = DateTimeOffset.UtcNow.AddDays(-Days).ToUnixTimeSeconds();
            query = query.Where(x => x.EndedAt >= from);
        }
        if (Game is not null) query = query.Where(x => x.GameName.Contains(Game));

        SessionCount = await query.CountAsync(ct);
        AccountCount = await query.Select(x => x.UserId).Distinct().CountAsync(ct);
        FpsSessionCount = await query.CountAsync(x => x.AverageFps != null && x.OnePercentLowFps != null, ct);
        InsufficientSessionCount = SessionCount - FpsSessionCount;
        LatestEndedAt = await query.MaxAsync(x => (long?)x.EndedAt, ct);

        var games = await query.GroupBy(x => x.GameName)
            .Select(g => new { Name = g.Key, Sessions = g.Count(), FpsSessions = g.Count(x => x.AverageFps != null && x.OnePercentLowFps != null),
                AverageFps = g.Average(x => x.AverageFps), OnePercentLowFps = g.Average(x => x.OnePercentLowFps) })
            .OrderByDescending(x => x.Sessions).ThenBy(x => x.Name).Take(30).ToListAsync(ct);
        Games = games.Select(x => new GameSummary(x.Name, x.Sessions, x.FpsSessions, x.AverageFps, x.OnePercentLowFps)).ToList();
        var gpus = await query.GroupBy(x => x.GpuName)
            .Select(g => new { Name = g.Key, Sessions = g.Count(), FpsSessions = g.Count(x => x.AverageFps != null && x.OnePercentLowFps != null) })
            .OrderByDescending(x => x.Sessions).ThenBy(x => x.Name).Take(20).ToListAsync(ct);
        Gpus = gpus.Select(x => new GpuSummary(x.Name, x.Sessions, x.FpsSessions)).ToList();
        var modes = await query.GroupBy(x => x.MuMode)
            .Select(g => new { Name = g.Key, Sessions = g.Count() })
            .OrderByDescending(x => x.Sessions).ThenBy(x => x.Name).ToListAsync(ct);
        Modes = modes.Select(x => new ModeSummary(x.Name, x.Sessions)).ToList();
        Recent = await query.OrderByDescending(x => x.EndedAt).ThenByDescending(x => x.Id).Take(50)
            .Select(x => new RecentSession(x.GameName, x.GpuName, x.MuMode, x.EndedAt,
                x.FrameCount, x.AverageFps, x.OnePercentLowFps)).ToListAsync(ct);
    }

    public sealed record GameSummary(string Name, int Sessions, int FpsSessions, double? AverageFps, double? OnePercentLowFps);
    public sealed record GpuSummary(string Name, int Sessions, int FpsSessions);
    public sealed record ModeSummary(string Name, int Sessions);
    public sealed record RecentSession(string GameName, string GpuName, string MuMode, long EndedAt,
        int FrameCount, double? AverageFps, double? OnePercentLowFps);
}
