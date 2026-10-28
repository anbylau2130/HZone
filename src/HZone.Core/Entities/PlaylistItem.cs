namespace HZone.Core.Entities;

/// <summary>
/// 播放列表项（歌单条目）：双音源——
/// media = 自己上传的音频资产（/media，引用 MediaAsset.Id）；
/// url = 外部音频直链（浏览器直接拉流）。SortOrder 小在前，重排端到端整体分配。
/// 多歌单：条目归属一张歌单（PlaylistId）。
/// </summary>
public class PlaylistItem
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>归属歌单（多歌单）；存量数据由迁移回填默认歌单。</summary>
    public Guid? PlaylistId { get; set; }

    public Playlist? Playlist { get; set; }

    /// <summary>"media" | "url"。</summary>
    public string SourceKind { get; set; } = "url";

    /// <summary>展示名：url 项必填；media 项缺省取资产 OriginalName。</summary>
    public string Title { get; set; } = null!;

    /// <summary>url 项的音频直链（http/https）；media 项为 null。</summary>
    public string? Url { get; set; }

    /// <summary>歌词原文（lrc 格式，用户录入/上传）；服务器目录虚拟项无此列不适用。</summary>
    public string? Lyrics { get; set; }

    /// <summary>media 项引用的音频资产；url 项为 null。</summary>
    public Guid? MediaAssetId { get; set; }

    public MediaAsset? MediaAsset { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
