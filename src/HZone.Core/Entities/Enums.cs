namespace HZone.Core.Entities;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1
}

public enum InboxMessageType
{
    Mentioned = 0,
    Commented = 1,
    FriendRequest = 2,
    Liked = 3
}

public enum MediaKind
{
    Image = 0,
    Video = 1,
    Audio = 2,
    /// <summary>普通附件（pdf/zip/office 等，见 MediaEndpoints 白名单）：以附件下载方式提供，不内联渲染。</summary>
    File = 3
}

public enum EmailTokenPurpose
{
    VerifyEmail = 0,
    ResetPassword = 1
}
