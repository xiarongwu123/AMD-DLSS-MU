using Mu.Compatibility;
namespace AmdNrAssistant;

// Existing server compatibility endpoints; retain the original authenticated contract.
public sealed partial class AccountApiClient
{
    public Task<CompatibilitySearchResponse> SearchCompatibilityAsync(string query,string? gpu,CancellationToken token) =>
        AuthenticatedAsync<CompatibilitySearchResponse>(HttpMethod.Get,"compatibility/games?q="+Uri.EscapeDataString(query)+
            (gpu==null?"":"&gpu="+Uri.EscapeDataString(gpu)),null,token);
    public Task<CompatibilityDetail> GetCompatibilityAsync(string gameId,string? gpu,CancellationToken token) =>
        AuthenticatedAsync<CompatibilityDetail>(HttpMethod.Get,"compatibility/games/"+Uri.EscapeDataString(gameId)+
            (gpu==null?"":"?gpu="+Uri.EscapeDataString(gpu)),null,token);
    public Task<CompatibilityTest> SubmitCompatibilityAsync(CompatibilitySubmission input,CancellationToken token) =>
        AuthenticatedAsync<CompatibilityTest>(HttpMethod.Post,"compatibility/tests",input,token);
}
