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

    /// <summary>音频元数据（上传时前端探测）：ID3 艺术家；无标签/非音频为 null。</summary>
    public string? Artist { get; set; }

    /// <summary>音频时长（秒）；探测失败为 null。</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>平均比特率 kbps（体积/时长估算）；探测失败为 null。</summary>
    public int? BitrateKbps { get; set; }

    public string? ThumbStoredName { get; set; }

    public Guid? ContentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Content? Content { get; set; }
}
