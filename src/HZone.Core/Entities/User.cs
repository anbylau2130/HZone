namespace HZone.Core.Entities;

public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Nickname { get; set; } = null!;

    public bool EmailVerified { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; }
}
