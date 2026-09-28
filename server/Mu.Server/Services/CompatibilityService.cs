using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mu.Compatibility;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class CompatibilityService(AppDbContext db, AccountService accounts, TimeProvider clock)
{
    private static readonly string[] FailureReasons =
        ["startup_crash", "load_failed", "menu_missing", "game_update", "anti_cheat", "visual_artifacts", "performance", "unknown"];

    private async Task RequireFeatureAsync(string userId, string key)
    {
        var account = await accounts.SnapshotAsync(userId);
        var feature = account.Features.SingleOrDefault(x => x.Key == key);
        if (feature?.Allowed == true) return;
        var reason = feature is null ? "unknown_feature" : !feature.Enabled ? "feature_disabled" : "pro_required";
        throw new ApiException(403, reason, reason == "pro_required" ? "此功能需要有效的 Pro 会员。" : "该功能暂不可用。");
    }

    public Task<CompatibilitySearchResponse> SearchAsync(string userId, string? query, string? gpu, CancellationToken ct = default)
        => SearchAsync(userId, query, gpu, 1, 50, ct);

    public async Task<CompatibilitySearchResponse> SearchAsync(string userId, string? query, string? gpu, int page, int pageSize, CancellationToken ct = default)
    {
        await RequireFeatureAsync(userId, "compatibility.read");
        return await SearchCoreAsync(query, gpu, page, pageSize, ct);
    }

    public Task<CompatibilitySearchResponse> PublicSearchAsync(string? query, string? gpu, CancellationToken ct = default)
        => PublicSearchAsync(query, gpu, 1, 50, ct);

    public async Task<CompatibilitySearchResponse> PublicSearchAsync(string? query, string? gpu, int page, int pageSize, CancellationToken ct = default)
    {
        await RequirePublicReadAsync(ct);
        return await SearchCoreAsync(query, gpu, page, pageSize, ct);
    }

    public async Task<CompatibilityDetail> PublicDetailAsync(string gameId, string? gpu, CancellationToken ct = default)
    {
        await RequirePublicReadAsync(ct);
        return await DetailCoreAsync(gameId, gpu, ct);
    }

    public async Task<string[]> PublicGpusAsync(CancellationToken ct = default)
    {
        await RequirePublicReadAsync(ct);
        return await db.CompatibilityTests.AsNoTracking().Where(x => x.GpuKey != "UNKNOWN")
            .GroupBy(x => x.GpuKey).OrderBy(x => x.Key).Select(x => x.Min(t => t.GpuName)!).Take(200).ToArrayAsync(ct);
    }

    private async Task RequirePublicReadAsync(CancellationToken ct)
    {
        // Public visibility is an independent on/off switch; membership does not apply to anonymous readers.
        if (!await db.FeatureDefinitions.AsNoTracking().AnyAsync(x => x.Key == "compatibility.public.read" && x.Enabled, ct))
            throw new ApiException(403, "feature_disabled", "兼容性公开查询暂不可用。");
    }

    private async Task<CompatibilitySearchResponse> SearchCoreAsync(string? query, string? gpu, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new ApiException(400, "invalid_pagination", "页码须大于 0，每页数量须为 1 至 50。");
        var search = Text(query, 200, false).ToUpperInvariant();
        var gpuKey = Text(gpu, 160, false).ToUpperInvariant();
        var catalog = db.CompatibilityGames.AsNoTracking();
        var matching = catalog.Where(x => x.SearchText.Contains(search));
        var catalogTotal = await catalog.CountAsync(ct);
        var coverage = CompatibilityMetadata.Coverage(await catalog.Select(x => x.SteamAppId).ToArrayAsync(ct));
        var matchingTotal = search.Length == 0 ? catalogTotal : await matching.CountAsync(ct);
        var games = await matching.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize).ToArrayAsync(ct);
        var ids = games.Select(x => x.Id).ToArray();
        var tests = db.CompatibilityTests.AsNoTracking().Where(x => ids.Contains(x.GameId));
        if (gpuKey.Length != 0) tests = tests.Where(x => x.GpuKey == gpuKey);
        var totals = await tests.GroupBy(x => x.GameId).Select(x => new
        {
            GameId = x.Key, Success = x.Count(t => t.Result == "success"), Partial = x.Count(t => t.Result == "partial"),
            Failure = x.Count(t => t.Result == "failure"), LastTestedAt = x.Max(t => t.CreatedAt)
        }).ToDictionaryAsync(x => x.GameId, ct);
        return new(games.Select(game =>
        {
            totals.TryGetValue(game.Id, out var total);
            var counts = new CompatibilityCounts(total?.Success ?? 0, total?.Partial ?? 0, total?.Failure ?? 0);
            return new CompatibilitySummary(Game(game), counts, Status(counts), At(total?.LastTestedAt),
                CompatibilityMetadata.Evidence(game.SteamAppId));
        }).ToArray(), matchingTotal, page, pageSize, catalogTotal, coverage);
    }

    public async Task<CompatibilityDetail> DetailAsync(string userId, string gameId, string? gpu, CancellationToken ct = default)
    {
        await RequireFeatureAsync(userId, "compatibility.read");
        return await DetailCoreAsync(gameId, gpu, ct);
    }

    private async Task<CompatibilityDetail> DetailCoreAsync(string gameId, string? gpu, CancellationToken ct)
    {
        gameId = Text(gameId, 64, true);
        var gpuKey = Text(gpu, 160, false).ToUpperInvariant();
        var game = await db.CompatibilityGames.AsNoTracking().SingleOrDefaultAsync(x => x.Id == gameId, ct)
            ?? throw new ApiException(404, "game_not_found", "游戏不存在。");
        var all = db.CompatibilityTests.AsNoTracking().Where(x => x.GameId == gameId);
        var totals = await all.GroupBy(x => x.GpuKey).Select(x => new
        {
            GpuKey = x.Key, GpuName = x.Min(t => t.GpuName)!, Success = x.Count(t => t.Result == "success"),
            Partial = x.Count(t => t.Result == "partial"), Failure = x.Count(t => t.Result == "failure"), LastTestedAt = x.Max(t => t.CreatedAt)
        }).ToArrayAsync(ct);
        var filtered = totals.Where(x => gpuKey.Length == 0 || x.GpuKey == gpuKey).ToArray();
        var counts = new CompatibilityCounts(filtered.Sum(x => x.Success), filtered.Sum(x => x.Partial), filtered.Sum(x => x.Failure));
        var gpus = totals.OrderBy(x => x.GpuName).Select(x =>
        {
            var count = new CompatibilityCounts(x.Success, x.Partial, x.Failure);
            return new CompatibilityGpuSummary(x.GpuName, count, Status(count), At(x.LastTestedAt));
        }).ToArray();
        if (gpuKey.Length != 0) all = all.Where(x => x.GpuKey == gpuKey);
        var recent = await all.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(50).ToArrayAsync(ct);
        return new(Game(game), counts, Status(counts), At(filtered.Length == 0 ? null : filtered.Max(x => x.LastTestedAt)),
            gpus, recent.Select(Test).ToArray(), CompatibilityMetadata.Requirements(game.SteamAppId),
            CompatibilityMetadata.Adaptations(game.SteamAppId));
    }

    public async Task<CompatibilityTest> SubmitAsync(string userId, CompatibilitySubmission input, CancellationToken ct = default)
    {
        input = Validate(input);
        var submissionHash = Hash(JsonSerializer.Serialize(input));
        // SQLite's immediate transaction serializes quota checks, catalog creation and duplicate inserts.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RequireFeatureAsync(userId, "compatibility.submit");
        var existing = await db.CompatibilityTests.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.SubmissionId == input.SubmissionId, ct);
        if (existing != null)
        {
            if (existing.SubmissionHash != submissionHash)
                throw new ApiException(409, "submission_conflict", "此提交标识已用于另一份测试结果，请重新提交。");
            return Test(existing);
        }
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var recent = await db.CompatibilityTests.Where(x => x.UserId == userId && x.CreatedAt > now - 86400)
            .Select(x => x.CreatedAt).ToArrayAsync(ct);
        var hourly = recent.Where(x => x > now - 3600).ToArray();
        if (recent.Length >= 100 || hourly.Length >= 20)
        {
            var dailyWait = recent.Length >= 100 ? recent.Min() + 86400 - now : 0;
            var hourlyWait = hourly.Length >= 20 ? hourly.Min() + 3600 - now : 0;
            throw new ApiException(429, "compatibility_rate_limited", "最近 24 小时或本小时提交次数已达上限，请稍后再试。", (int)Math.Max(1, Math.Max(dailyWait, hourlyWait)));
        }
        var game = await ResolveGameAsync(input, now, ct);
        var environmentJson = JsonSerializer.Serialize(input.Environment);
        var environmentHash = Hash(environmentJson.ToUpperInvariant());
        var day = now / 86400;
        if (await db.CompatibilityTests.AnyAsync(x => x.UserId == userId && x.GameId == game.Id && x.EnvironmentHash == environmentHash && x.TestDay == day, ct))
            throw new ApiException(409, "environment_already_reported", "此账号今天已提交过该游戏和测试环境，未重复计数。");
        var row = new CompatibilityTestEntry
        {
            UserId = userId, SubmissionId = input.SubmissionId, SubmissionHash = submissionHash, GameId = game.Id,
            GpuName = input.Environment.Gpu.Name, GpuKey = input.Environment.Gpu.Name.ToUpperInvariant(),
            EnvironmentJson = environmentJson, EnvironmentHash = environmentHash, Result = input.Result,
            FailureReason = input.FailureReason, Notes = input.Notes, CreatedAt = now, TestDay = day
        };
        db.CompatibilityTests.Add(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Test(row);
    }

    private async Task<CompatibilityGameEntry> ResolveGameAsync(CompatibilitySubmission input, long now, CancellationToken ct)
    {
        var nameKey = input.GameName.ToUpperInvariant();
        if (input.GameId != null)
        {
            var selected = await db.CompatibilityGames.SingleOrDefaultAsync(x => x.Id == input.GameId, ct)
                ?? throw new ApiException(404, "game_not_found", "游戏不存在，请重新选择。");
            if ((input.SteamAppId != null && input.SteamAppId != selected.SteamAppId)
                || (input.SteamAppId == null && nameKey != selected.NormalizedName))
                throw new ApiException(409, "game_identity_mismatch", "所选游戏与本机游戏标识不匹配，请重新选择正确版本。");
            return selected;
        }
        if (input.SteamAppId != null)
        {
            var steam = await db.CompatibilityGames.SingleOrDefaultAsync(x => x.SteamAppId == input.SteamAppId, ct);
            if (steam != null) return steam;
        }
        else
        {
            var matches = await db.CompatibilityGames.Where(x => x.NormalizedName == nameKey).Take(2).ToArrayAsync(ct);
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) throw new ApiException(409, "game_identity_ambiguous", "该名称存在多个游戏版本，请先选择准确的游戏。");
        }
        var game = new CompatibilityGameEntry
        {
            Name = input.GameName, NormalizedName = nameKey, SteamAppId = input.SteamAppId,
            SearchText = nameKey + " " + input.SteamAppId, CreatedAt = now
        };
        db.CompatibilityGames.Add(game);
        return game;
    }

    private static CompatibilitySubmission Validate(CompatibilitySubmission? input)
    {
        if (input?.Environment?.Gpu == null) throw Invalid();
        var submissionId = Text(input.SubmissionId, 64, true);
        if (!Guid.TryParse(submissionId, out var submissionGuid)) throw Invalid();
        var steamId = Optional(input.SteamAppId, 12);
        if (steamId != null && (!uint.TryParse(steamId, out var appId) || appId == 0 || !steamId.All(char.IsAsciiDigit))) throw Invalid();
        if (steamId != null) steamId = uint.Parse(steamId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = Text(input.Result, 16, true).ToLowerInvariant();
        if (result is not ("success" or "partial" or "failure")) throw Invalid();
        var reason = Optional(input.FailureReason, 32)?.ToLowerInvariant();
        if ((reason != null && !FailureReasons.Contains(reason)) || (result == "success" && reason != null)) throw Invalid();
        if (result != "success" && reason == null) reason = "unknown";
        var env = input.Environment;
        var gpu = env.Gpu;
        if (gpu.VramMb is < 0 or > 1048576) throw Invalid();
        var api = Text(env.RenderApi, 16, true);
        api = api.ToUpperInvariant() switch { "DX11" => "DX11", "DX12" => "DX12", "VULKAN" => "Vulkan", "UNKNOWN" => "unknown", _ => throw Invalid() };
        var environment = new CompatibilityEnvironment(new(Text(gpu.Name, 160, true), Text(gpu.Vendor, 64, true),
            gpu.VramMb, Optional(gpu.Architecture, 64), Text(gpu.DriverVersion, 100, true)),
            Text(env.OsVersion, 200, true), Text(env.SystemDirectX, 100, true), Text(env.GameVersion, 100, true),
            api, Text(env.DlssVersion, 100, true), Text(env.Dlss5Version, 100, true), Text(env.ToolVersion, 100, true),
            Text(env.Settings, 2000, false), Text(env.OtherMods, 1000, false));
        return new(submissionGuid.ToString("N"), Optional(input.GameId, 64), Text(input.GameName, 200, true), steamId,
            environment, result, reason, Optional(input.Notes, 2000));
    }

    private static string Text(string? value, int max, bool required)
    {
        value = value?.Trim() ?? "";
        if (value.Length > max || (required && value.Length == 0) || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw Invalid();
        return value;
    }
    private static string? Optional(string? value, int max) => Text(value, max, false) is { Length: > 0 } text ? text : null;
    private static ApiException Invalid() => new(400, "invalid_compatibility_submission", "测试数据无效，请检查游戏、环境与结果。无法读取的环境信息可填 unknown。");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DateTimeOffset? At(long? value) => value.HasValue ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;
    private static CompatibilityGame Game(CompatibilityGameEntry value) => new(value.Id, value.Name, value.SteamAppId, value.Developer, value.Engine,
        CompatibilityCatalog.FindInfo(value.SteamAppId));
    private static CompatibilityTest Test(CompatibilityTestEntry row) => new(row.Id, row.GameId, "tester-" + Hash("compatibility:" + row.UserId)[..12].ToLowerInvariant(),
        JsonSerializer.Deserialize<CompatibilityEnvironment>(row.EnvironmentJson)!, row.Result, row.FailureReason, row.Notes, DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt));
    private static string Status(CompatibilityCounts counts) => counts.Total == 0 ? "untested" : counts.Success == counts.Total ? "success"
        : counts.Failure == counts.Total ? "failure" : counts.Partial == counts.Total ? "partial" : "mixed";
}
