namespace HZone.Core.Entities;

/// <summary>
/// 内容点赞：可见即可赞（作者本人也可），再点撤销（切换语义）。
/// 每人每内容至多一行（唯一索引），随内容级联删除。
/// </summary>
public class ContentLike
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public Content? Content { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
