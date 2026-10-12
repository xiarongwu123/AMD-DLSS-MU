using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Mu.Presets;
using Mu.Server.Data;

namespace Mu.Server.Services;

public sealed class CommunityPresetService(AppDbContext db, TimeProvider clock)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static string Author(string userId) => "玩家 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..6].ToLowerInvariant();
    long Now => clock.GetUtcNow().ToUnixTimeSeconds();

    static string GameKey(string? value)
    {
        value = value?.Trim();
        if (value == null || !(Regex.IsMatch(value, "^steam:[0-9]{1,12}$", RegexOptions.CultureInvariant)
            || Regex.IsMatch(value, "^title:[A-F0-9]{24}$", RegexOptions.CultureInvariant)))
            throw new ApiException(400, "invalid_preset_game", "游戏标识无效。");
        return value;
    }

    static PresetItem Item(CommunityPresetEntry entry, string userId)
    {
        var count = JsonSerializer.Deserialize<PresetSetting[]>(entry.SettingsJson, Json)?.Length ?? 0;
        return new(entry.Id, entry.GameKey, entry.GameName, entry.Mode, entry.Goal, entry.Vulkan,
            entry.ComponentVersion, entry.Title, entry.Description, entry.Author,
            entry.CreatedAt, entry.ImageData != null, entry.UserId == userId, count);
    }

    public async Task<PresetList> ListAsync(string userId, string? gameKey, CancellationToken ct)
    {
        var key = GameKey(gameKey);
        var query = db.CommunityPresets.AsNoTracking().Where(p => p.GameKey == key);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Take(50).ToArrayAsync(ct);
        return new(rows.Select(p => Item(p, userId)).ToArray(), total);
    }

    public async Task<PresetDetail> DetailAsync(string userId, string id, CancellationToken ct)
    {
        var row = await db.CommunityPresets.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ApiException(404, "preset_not_found", "方案不存在。");
        return new(Item(row, userId), JsonSerializer.Deserialize<PresetSetting[]>(row.SettingsJson, Json) ?? [], row.ImageData);
    }

    public async Task<PresetDetail> SubmitAsync(string userId, PresetSubmission input, CancellationToken ct)
    {
        if (!Guid.TryParse(input.SubmissionId, out _) || input.Title is null || input.Title.Trim().Length is < 3 or > 50 ||
            input.Description is null || input.Description.Trim().Length is < 1 or > 300 ||
            input.GameName is null || input.GameName.Trim().Length is < 1 or > 100 ||
            input.ComponentVersion is null || input.ComponentVersion.Trim().Length is < 1 or > 100 ||
            input.Goal is not ("quality" or "performance" or "custom") ||
            input.Mode is not (0 or 2 or 4) ||
            (input.Vulkan && input.Mode != 2) ||
            (input.Goal == "quality" && input.Mode != 0) ||
            (input.Goal == "performance" && input.Mode != 4))
            throw new ApiException(400, "invalid_preset", "请检查方案标题、说明和类型。");
        var key = GameKey(input.GameKey);
        PresetSetting[] settings;
        try { settings = PresetRules.Validate(input.Mode, input.Settings); }
        catch (InvalidDataException) { throw new ApiException(400, "invalid_preset_settings", "方案包含不支持的参数。"); }
        if (!PresetRules.VulkanCompatible(input.Mode, input.Vulkan, settings))
            throw new ApiException(400, "invalid_preset_settings", "Vulkan 方案不能分享当前帧生成设置。");
        ValidateImage(input.ImageData);
        var previous = await db.CommunityPresets.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId && p.SubmissionId == input.SubmissionId, ct);
        if (previous != null)
        {
            if (previous.GameKey != key || previous.GameName != input.GameName.Trim() || previous.Mode != input.Mode ||
                previous.Goal != input.Goal || previous.Vulkan != input.Vulkan ||
                previous.ComponentVersion != input.ComponentVersion.Trim() || previous.Title != input.Title.Trim() ||
                previous.Description != input.Description.Trim() || previous.SettingsJson != JsonSerializer.Serialize(settings, Json) ||
                previous.ImageData != input.ImageData)
                throw new ApiException(409, "preset_submission_conflict", "这次提交的内容已改变，请重新打开分享窗口再试。");
            return await DetailAsync(userId, previous.Id, ct);
        }
        if (await db.CommunityPresets.CountAsync(p => p.UserId == userId && p.CreatedAt > Now - 86400, ct) >= 5)
            throw new ApiException(429, "preset_rate_limited", "每天最多分享 5 个方案。", 3600);
        var row = new CommunityPresetEntry
        {
            UserId = userId, SubmissionId = input.SubmissionId, GameKey = key,
            GameName = input.GameName.Trim(), Mode = input.Mode, Goal = input.Goal, Vulkan = input.Vulkan,
            ComponentVersion = input.ComponentVersion.Trim(), Title = input.Title.Trim(),
            Description = input.Description.Trim(), Author = Author(userId), CreatedAt = Now,
            SettingsJson = JsonSerializer.Serialize(settings, Json), ImageData = input.ImageData
        };
        db.CommunityPresets.Add(row);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(userId, row.Id, ct);
    }

    static void ValidateImage(string? data)
    {
        if (data == null) return;
        if (data.Length > 280000 || !data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal))
            throw new ApiException(400, "invalid_preset_image", "截图须为压缩后的 JPEG 图片，且不超过 200 KB。");
        try
        {
            var bytes = Convert.FromBase64String(data["data:image/jpeg;base64,".Length..]);
            if (bytes.Length < 4 || bytes.Length > 200000 || bytes[0] != 0xff || bytes[1] != 0xd8 ||
                bytes[^2] != 0xff || bytes[^1] != 0xd9)
                throw new ApiException(400, "invalid_preset_image", "截图格式无效。");
        }
        catch (FormatException) { throw new ApiException(400, "invalid_preset_image", "截图格式无效。"); }
    }
}
