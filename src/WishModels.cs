namespace Mu.Wishes;

public sealed record WishSubmission(string SubmissionId, string Content, string? ImageData = null);
public sealed record WishVoteRequest(bool Voted);
public sealed record WishCommentRequest(string Content);
public sealed record WishItem(string Id, string Title, string Content, string Status, string Author,
    long CreatedAt, int Votes, int Comments, bool Voted, bool IsMine, bool IsDemo, bool HasImage);
public sealed record WishCounts(int All, int Hot, int Planned, int Developing, int Completed, int Mine);
public sealed record WishList(WishItem[] Items, WishCounts Counts, int Total, int Page, int PageSize);
public sealed record WishComment(string Id, string Author, string Content, long CreatedAt, bool IsDemo);
public sealed record WishDetail(WishItem Wish, string? ImageData, WishComment[] Comments);
