using Mu.Telemetry;

namespace AmdNrAssistant;

public sealed partial class AccountApiClient
{
    public Task<GameTelemetryReceipt> SubmitTelemetryAsync(GameTelemetryUpload session, CancellationToken token) =>
        AuthenticatedAsync<GameTelemetryReceipt>(HttpMethod.Post, "telemetry/sessions", session, token);

    public Task DeleteTelemetryAsync(CancellationToken token) =>
        AuthenticatedAsync<object?>(HttpMethod.Delete, "telemetry/sessions", null, token);
}
