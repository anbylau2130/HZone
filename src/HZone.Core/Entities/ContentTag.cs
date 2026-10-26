namespace HZone.Core.Entities;

/// <summary>
/// 内容标签：作者为内容打的分类标记，用于跨时间的主题检索
///（如「关于爸爸的记录」）。每内容每标签唯一，整体替换语义
///（保存时传全量标签数组）。Tag 规范化：trim 后非空、≤32 字符。
/// </summary>
public class ContentTag
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public Content? Content { get; set; }

    public string Tag { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
