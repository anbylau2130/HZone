namespace HZone.Core.Entities;

public class InviteCode
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public Guid? UsedById { get; set; }

    public DateTime? UsedAt { get; set; }
}
