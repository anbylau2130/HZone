namespace HZone.Core.Entities;

/// <summary>
/// 歌单（多歌单）：每用户可建多张；曲目 PlaylistItem 归属一张歌单。
/// 存量单表时代的数据由迁移回填为每人一张「默认歌单」。
/// </summary>
public class Playlist
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>歌单名（1~50 字符，端点校验）。</summary>
    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public List<PlaylistItem> Items { get; set; } = [];
}
