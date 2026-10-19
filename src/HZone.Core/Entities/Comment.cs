namespace HZone.Core.Entities;

public class Comment
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public Guid AuthorId { get; set; }

    public string Text { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Content Content { get; set; } = null!;

    public User Author { get; set; } = null!;
}
