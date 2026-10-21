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

    /// <summary>个人签名（≤100 字），moments/profile 页签名档用；null = 未设置。</summary>
    public string? Signature { get; set; }

    /// <summary>
    /// 封面媒体 storedName（与 MediaAsset.StoredName 同口径，读取走 GET /media/{storedName}）；
    /// DTO 下发时统一投影为 /media/{storedName}。null = 默认渐变。
    /// </summary>
    public string? CoverUrl { get; set; }

    /// <summary>
    /// 账号级偏好（PRD 6.9）：整包 JSON 字符串（{ mode, presetId, overrides, locale }），
    /// 后端不解析结构，仅限长 ≤4KB（PUT /me/preferences 写入）。null = 从未设置。
    /// </summary>
    public string? Preferences { get; set; }

    public bool EmailVerified { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; }
}
