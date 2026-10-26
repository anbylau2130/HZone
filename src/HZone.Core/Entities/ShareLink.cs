namespace HZone.Core.Entities;

/// <summary>
/// 只读分享外链：作者为单条内容签发的匿名只读凭证（给未注册的家人看）。
/// Token 256-bit 随机 base64url，**只存 SHA-256 哈希**（明文仅创建响应返回一次，
/// PAT 同模式）——库泄漏不泄漏能力凭证。≤30 天过期，可撤销；内容删除级联。
/// </summary>
public class ShareLink
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public Content? Content { get; set; }

    public Guid OwnerUserId { get; set; }

    /// <summary>SHA-256 hex（唯一索引；查询按哈希找）。</summary>
    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
