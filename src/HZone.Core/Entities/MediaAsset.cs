namespace HZone.Core.Entities;

public class MediaAsset
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string StoredName { get; set; } = null!;

    public string OriginalName { get; set; } = null!;

    public MediaKind Kind { get; set; }

    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string? ThumbStoredName { get; set; }

    public Guid? ContentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Content? Content { get; set; }
}
