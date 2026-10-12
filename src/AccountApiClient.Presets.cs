using Mu.Presets;

namespace AmdNrAssistant;

public sealed partial class AccountApiClient
{
    public Task<PresetList> GetPresetsAsync(string gameKey, CancellationToken token) =>
        AuthenticatedAsync<PresetList>(HttpMethod.Get, "presets?gameKey=" + Uri.EscapeDataString(gameKey), null, token);

    public Task<PresetDetail> GetPresetAsync(string id, CancellationToken token) =>
        AuthenticatedAsync<PresetDetail>(HttpMethod.Get, "presets/" + Uri.EscapeDataString(id), null, token);

    public Task<PresetDetail> SubmitPresetAsync(PresetSubmission input, CancellationToken token) =>
        AuthenticatedAsync<PresetDetail>(HttpMethod.Post, "presets", input, token);
}
