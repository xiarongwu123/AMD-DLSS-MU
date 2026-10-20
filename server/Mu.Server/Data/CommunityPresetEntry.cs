namespace Mu.Server.Data;

public sealed class CommunityPresetEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string SubmissionId { get; set; } = "";
    public string GameKey { get; set; } = "";
    public string GameName { get; set; } = "";
    public int Mode { get; set; }
    public string Goal { get; set; } = "";
    public bool Vulkan { get; set; }
    public string ComponentVersion { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public long CreatedAt { get; set; }
    public string SettingsJson { get; set; } = "[]";
    public string? ImageData { get; set; }
    public string? ThumbnailData { get; set; }
    public string Scope { get; set; } = "game";
    public string? ResourceUrl { get; set; }
    public string? ResourceCode { get; set; }
    public string? AttachmentName { get; set; }
    public int AttachmentSize { get; set; }
    public string? AttachmentSha256 { get; set; }
}

public sealed class CommunityPresetAttachment
{
    public string PresetId { get; set; } = "";
    public byte[] Content { get; set; } = [];
}
