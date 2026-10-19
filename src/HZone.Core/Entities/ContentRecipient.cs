namespace HZone.Core.Entities;

public class ContentRecipient
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public Guid RecipientUserId { get; set; }

    public DateTime AddedAt { get; set; }

    public Content Content { get; set; } = null!;

    public User User { get; set; } = null!;
}
