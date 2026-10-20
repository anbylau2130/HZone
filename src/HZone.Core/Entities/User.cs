namespace HZone.Core.Entities;

public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Nickname { get; set; } = null!;

    /// <summary>
    /// 头像媒体 storedName（与 MediaAsset.StoredName 同口径，读取走 GET /media/{storedName}）；
    /// DTO 下发时统一投影为 /media/{storedName}。null = 未设置。
    /// </summary>
    public string? AvatarUrl { get; set; }

    public bool EmailVerified { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; }
}
