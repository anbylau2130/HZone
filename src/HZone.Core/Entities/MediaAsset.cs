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

    /// <summary>
    /// 音频共享标记：私有（默认，仅本人）= false；共享 = 所有人可在音频库见到并入列表。
    /// 仅音频有意义，其它类型恒 false。
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>
    /// 物理子目录标记：null = 老位置 data/media；"@music" = 音乐根目录（共享）；
    /// "@music/{昵称}" = 音乐根下的用户私有目录。运行时按 musicDir 配置解析。
    /// </summary>
    public string? Subdir { get; set; }

    public string? ThumbStoredName { get; set; }

    public Guid? ContentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Content? Content { get; set; }
}
