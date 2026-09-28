using Mu.Compatibility;

namespace AmdNrAssistant;

public sealed partial class AccountApiClient
{
    public Task<CompatibilitySearchResponse> SearchCompatibilityAsync(string query, string? gpu, CancellationToken token) =>
        AuthenticatedAsync<CompatibilitySearchResponse>(HttpMethod.Get,
            "compatibility/games?q=" + Uri.EscapeDataString(query ?? "") + GpuQuery(gpu, "&"), null, token);

    public Task<CompatibilityDetail> GetCompatibilityAsync(string gameId, string? gpu, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        return AuthenticatedAsync<CompatibilityDetail>(HttpMethod.Get,
            "compatibility/games/" + Uri.EscapeDataString(gameId) + GpuQuery(gpu, "?"), null, token);
    }

    public Task<CompatibilityTest> SubmitCompatibilityAsync(CompatibilitySubmission submission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.SubmissionId);
        // Keep the caller's ID and body intact when AuthenticatedAsync refreshes and retries.
        return AuthenticatedAsync<CompatibilityTest>(HttpMethod.Post, "compatibility/tests", submission, token);
    }

    static string GpuQuery(string? gpu, string separator) => string.IsNullOrWhiteSpace(gpu)
        ? "" : separator + "gpu=" + Uri.EscapeDataString(gpu.Trim());
}
