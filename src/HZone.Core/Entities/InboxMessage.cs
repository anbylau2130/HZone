namespace HZone.Core.Entities;

public class InboxMessage
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public InboxMessageType Type { get; set; }

    /// <summary>内容类站内信关联的内容 id；FriendRequest 等非内容类站内信为 null。</summary>
    public Guid? ContentId { get; set; }

    /// <summary>触发人（点赞人等；老数据/非触发类为 null）。点赞通知按 (UserId, ContentId, Actor) 去重。</summary>
    public Guid? ActorUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public User User { get; set; } = null!;

    public Content? Content { get; set; }
}
