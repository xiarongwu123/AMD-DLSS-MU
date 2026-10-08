using Mu.Wishes;

namespace AmdNrAssistant;

public sealed partial class AccountApiClient
{
    public Task<WishList> GetWishesAsync(string filter, string query, string sort, int page, CancellationToken token) =>
        AuthenticatedAsync<WishList>(HttpMethod.Get, $"wishes?filter={Uri.EscapeDataString(filter)}&q={Uri.EscapeDataString(query)}&sort={Uri.EscapeDataString(sort)}&page={page}", null, token);

    public Task<WishDetail> GetWishAsync(string id, CancellationToken token) =>
        AuthenticatedAsync<WishDetail>(HttpMethod.Get, "wishes/" + Uri.EscapeDataString(id), null, token);

    public Task<WishDetail> SubmitWishAsync(WishSubmission input, CancellationToken token) =>
        AuthenticatedAsync<WishDetail>(HttpMethod.Post, "wishes", input, token);

    public Task<WishItem> VoteWishAsync(string id, bool voted, CancellationToken token) =>
        AuthenticatedAsync<WishItem>(HttpMethod.Put, "wishes/" + Uri.EscapeDataString(id) + "/vote", new WishVoteRequest(voted), token);

    public Task<WishComment> CommentWishAsync(string id, string content, CancellationToken token) =>
        AuthenticatedAsync<WishComment>(HttpMethod.Post, "wishes/" + Uri.EscapeDataString(id) + "/comments", new WishCommentRequest(content), token);
}
