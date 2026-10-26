using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Telemetry;

namespace Mu.Server;

public static class TelemetryEndpoints
{
    public static void MapTelemetryApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/telemetry").RequireAuthorization("Client").RequireRateLimiting("telemetry");
        api.MapPost("/sessions", async (GameTelemetryUpload input, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var error = Validate(input);
            if (error != null) return Results.BadRequest(new { code = "invalid_telemetry", message = error });
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var payload = JsonSerializer.Serialize(input);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            var existing = await db.GameTelemetry.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Id == input.SessionId, ct);
            if (existing != null) return existing.PayloadHash == hash
                ? Results.Ok(new GameTelemetryReceipt(input.SessionId, false))
                : Results.Conflict(new { code = "session_conflict", message = "会话 ID 已用于不同数据。" });
            db.GameTelemetry.Add(new GameTelemetryEntry
            {
                Id = input.SessionId, UserId = userId, GameName = input.GameName.Trim(),
                GpuName = input.GpuName ?? "unknown", MuMode = input.MuMode ?? "none",
                StartedAt = input.StartedAt.ToUnixTimeSeconds(), EndedAt = input.EndedAt.ToUnixTimeSeconds(),
                FrameCount = input.FrameCount, AverageFps = input.AverageFps,
                OnePercentLowFps = input.OnePercentLowFps,
                PayloadJson = payload, PayloadHash = hash
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                existing = await db.GameTelemetry.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Id == input.SessionId, ct);
                if (existing != null) return existing.PayloadHash == hash
                    ? Results.Ok(new GameTelemetryReceipt(input.SessionId, false))
                    : Results.Conflict(new { code = "session_conflict", message = "会话 ID 已用于不同数据。" });
                throw;
            }
            return Results.Ok(new GameTelemetryReceipt(input.SessionId, true));
        });
        api.MapDelete("/sessions", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await db.GameTelemetry.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }

    internal static string? Validate(GameTelemetryUpload x)
    {
        if(x.Recommendation is {} r)
        {
            if(r.Active||r.ExperienceConfirmed)return "当前没有已接入的可核验运行时执行证明，不能将记录上报为ACTIVE或EXPERIENCE_CONFIRMED";
            var texts=new[]{r.GameId,r.GameBuild,r.GpuModel,r.Architecture,r.DriverVersion,r.Api,r.Scheme,r.Backend,r.ComponentVersion,r.PresetId,r.RuleRevision,r.Feedback,r.GameVersion,r.GpuDeviceId};
            if(texts.Any(v=>v==null||v.Length>160||v.Any(char.IsControl)||v.Contains(":")&&v.Contains("\\")||v.StartsWith('/')||v.StartsWith('\\'))||
                !System.Text.RegularExpressions.Regex.IsMatch(r.DeviceId??"","^[A-Fa-f0-9]{64}$")||!Mu.Recommendation.RecommendationRuleSchema.Id(r.GameId)||
                !Mu.Recommendation.RecommendationRuleSchema.Schemes.Contains(r.Scheme)||r.Bitness is not (32 or 64)||
                r.FailureCodes==null||r.FailureCodes.Length>20||r.FailureCodes.Any(v=>!System.Text.RegularExpressions.Regex.IsMatch(v,"^[A-Z_]{1,80}$"))||
                r.Active&&!r.Loaded||r.Loaded&&!r.Installed||r.ExperienceConfirmed&&!r.Active||r.VerifiedAt>x.EndedAt||r.VerifiedAt<x.StartedAt)
                return "无效的推荐验证记录；未知、安装、加载、生效必须分开记录";
        }
        if (!Guid.TryParse(x.SessionId, out _)) return "无效会话 ID";
        if (string.IsNullOrWhiteSpace(x.GameName) || x.GameName.Length > 160 || x.GameName.Any(char.IsControl)) return "无效游戏名称";
        if (new[] { x.SteamAppId, x.GameVersion, x.GpuName, x.DriverVersion, x.CpuName, x.WindowsVersion, x.MuMode, x.MuVersion, x.PresentRuntime, x.FpsSource, x.CaptureStatus }
            .Any(s => s is { Length: > 160 } || s?.Any(char.IsControl) == true ||
                s?.Contains(":\\") == true || s?.Contains(":/") == true ||
                s?.StartsWith('\\') == true || s?.StartsWith('/') == true)) return "字段包含无效内容";
        if (x.StartedAt > x.EndedAt || x.EndedAt - x.StartedAt > TimeSpan.FromHours(24) || x.EndedAt > DateTimeOffset.UtcNow.AddMinutes(10)) return "无效会话时间";
        if (x.FrameCount is < 0 or > 20_000_000 || x.VramMb is < 0 or > 2_000_000 || x.MemoryMb is < 0 or > 8_000_000) return "数值超出范围";
        foreach (var value in new[] { x.AverageFps, x.OnePercentLowFps, x.AverageGpuBusyMs, x.AverageCpuBusyMs })
            if (value is { } v && (!double.IsFinite(v) || v < 0 || v > 100_000)) return "无效性能数据";
        if ((x.AverageFps != null || x.OnePercentLowFps != null) && x.FrameCount < 120) return "帧数不足";
        return null;
    }
}
