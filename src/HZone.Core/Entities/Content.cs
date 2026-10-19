namespace HZone.Core.Entities;

public abstract class Content
{
    public Guid Id { get; set; }

    public Guid AuthorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<ContentRecipient> Recipients { get; set; } = new List<ContentRecipient>();
}
