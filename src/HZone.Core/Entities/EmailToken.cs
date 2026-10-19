namespace HZone.Core.Entities;

public class EmailToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public EmailTokenPurpose Purpose { get; set; }

    public string Token { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
