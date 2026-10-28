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

    /// <summary>原始上传字节 SHA-256 hex（上传幂等去重键；老数据为 null 不参与去重）。</summary>
    public string? ContentHash { get; set; }

    public string? ThumbStoredName { get; set; }

    public Guid? ContentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Content? Content { get; set; }
}
