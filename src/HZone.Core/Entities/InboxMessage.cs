namespace HZone.Core.Entities;

public class InboxMessage
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public InboxMessageType Type { get; set; }

    public Guid ContentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public User User { get; set; } = null!;

    public Content Content { get; set; } = null!;
}
