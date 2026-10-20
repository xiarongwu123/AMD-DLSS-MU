using Mu.Presets;

namespace AmdNrAssistant;

public sealed partial class AccountApiClient
{
    public async Task<PresetList> GetPresetsAsync(CancellationToken token, int page = 1)
    {
        try { return await AuthenticatedAsync<PresetList>(HttpMethod.Get, "presets/all?page=" + Math.Max(1, page), null, token); }
        catch (AccountApiException e) when (e.Status == System.Net.HttpStatusCode.NotFound || e.Code == "invalid_preset_game")
        { throw new IOException("全站玩家方案需要更新后台服务，请联系管理员同步部署；列表无需选择游戏。", e); }
    }

    public Task<PresetDetail> GetPresetAsync(string id, CancellationToken token) =>
        AuthenticatedAsync<PresetDetail>(HttpMethod.Get, "presets/" + Uri.EscapeDataString(id), null, token);

    public Task<PresetAttachmentDownload> GetPresetAttachmentAsync(string id, CancellationToken token) =>
        AuthenticatedAsync<PresetAttachmentDownload>(HttpMethod.Get, "presets/" + Uri.EscapeDataString(id) + "/attachment", null, token);

    public Task<PresetDetail> SubmitPresetAsync(PresetSubmission input, CancellationToken token) =>
        AuthenticatedAsync<PresetDetail>(HttpMethod.Post, "presets", input, token);
}
