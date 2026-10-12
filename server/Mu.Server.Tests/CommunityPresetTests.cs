using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mu.Presets;
using Mu.Server;
using Mu.Server.Data;
using Mu.Server.Services;

internal static class CommunityPresetTests
{
    public static async Task<int> RunAsync(ServiceProvider services, string userId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CommunityPresetSchema.InitializeAsync(db);
        var service = new CommunityPresetService(db, TimeProvider.System);
        var submission = new PresetSubmission(Guid.NewGuid().ToString("N"), "steam:12345", "测试游戏", 4,
            "performance", false, "DLSS5 + XeFG 2X (v1.10.3.1/v10.6)", "画面流畅方案", "测试分享与复用",
            null, [new("OptiScaler.ini", "XeFG", "InterpolationCount", "1")]);
        var saved = await service.SubmitAsync(userId, submission, default);
        if (saved.Item.Mode != 4 || saved.Settings.Length != 1 || saved.Item.GameKey != submission.GameKey)
            throw new Exception("Community preset was not stored correctly");
        var list = await service.ListAsync(userId, submission.GameKey, default);
        if (list.Total != 1 || list.Items[0].Id != saved.Item.Id)
            throw new Exception("Community preset was not listed for its game");
        var repeated = await service.SubmitAsync(userId, submission, default);
        if (repeated.Item.Id != saved.Item.Id || await db.CommunityPresets.CountAsync() != 1)
            throw new Exception("Community preset retry created a duplicate");
        try
        {
            await service.SubmitAsync(userId, submission with { Title = "修改后的标题" }, default);
            throw new Exception("Changed submission reused an idempotency key");
        }
        catch (ApiException e) when (e.Error.Code == "preset_submission_conflict") { }
        try
        {
            await service.SubmitAsync(userId, submission with
            {
                SubmissionId = Guid.NewGuid().ToString("N"),
                Settings = [new("OptiScaler.ini", "ProcessFilter", "TargetProcessName", "private.exe")]
            }, default);
            throw new Exception("Process path was accepted in a shared preset");
        }
        catch (ApiException e) when (e.Error.Code == "invalid_preset_settings") { }
        var tinyJpeg = "data:image/jpeg;base64," + Convert.ToBase64String([0xff, 0xd8, 0xff, 0xd9]);
        var screenshot = await service.SubmitAsync(userId, submission with
        {
            SubmissionId = Guid.NewGuid().ToString("N"), Title = "附带游戏截图的方案",
            ImageData = tinyJpeg, ThumbnailData = tinyJpeg
        }, default);
        if (screenshot.Item.ThumbnailData != tinyJpeg || screenshot.ImageData != tinyJpeg ||
            (await service.ListAsync(userId, submission.GameKey, default)).Items.All(x => x.ThumbnailData == null))
            throw new Exception("Community screenshot thumbnail was not preserved in the list");
        return 6;
    }
}
