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
    FriendRequest = 2
}

public enum MediaKind
{
    Image = 0,
    Video = 1,
    Audio = 2
}

public enum EmailTokenPurpose
{
    VerifyEmail = 0,
    ResetPassword = 1
}
