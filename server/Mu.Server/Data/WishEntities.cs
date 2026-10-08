namespace Mu.Server.Data;

public sealed class WishEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public string SubmissionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Author { get; set; } = "";
    public long CreatedAt { get; set; }
    public bool IsDemo { get; set; }
    public int DemoVotes { get; set; }
    public string? ImageData { get; set; }
}

public sealed class WishVoteEntry
{
    public string WishId { get; set; } = "";
    public string UserId { get; set; } = "";
}

public sealed class WishCommentEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WishId { get; set; } = "";
    public string? UserId { get; set; }
    public string Author { get; set; } = "";
    public string Content { get; set; } = "";
    public long CreatedAt { get; set; }
    public bool IsDemo { get; set; }
}
