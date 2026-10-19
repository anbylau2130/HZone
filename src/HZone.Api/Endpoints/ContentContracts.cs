using HZone.Core.Entities;

namespace HZone.Api.Endpoints;

public record CreateSayRequest(string Text, List<Guid> RecipientIds, List<Guid>? MediaIds = null);

public record CreateJournalRequest(string Title, string Markdown, List<Guid> RecipientIds);

/// <summary>
/// 内容编辑：只更新显式提供的字段（null = 不修改）。recipientIds 非空时整体替换可见名单
/// （story 14）：新增者收 Mentioned 站内信，被移除者立即失权。
/// </summary>
public record UpdateContentRequest(
    string? Text = null,
    string? Title = null,
    string? MarkdownBody = null,
    List<Guid>? MediaIds = null,
    List<Guid>? RecipientIds = null);

public record RecipientResponse(Guid UserId, string Nickname);

public record AuthorResponse(Guid Id, string Nickname);

public record MediaResponse(Guid Id, string Kind, string Url, string? ThumbUrl, long SizeBytes);

/// <summary>
/// 统一内容 DTO：详情/列表/创建响应同形。
/// </summary>
public record ContentResponse(
    Guid Id,
    string Kind,
    AuthorResponse Author,
    string? Text,
    string? Title,
    string? MarkdownBody,
    List<RecipientResponse> Recipients,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int CommentCount,
    List<MediaResponse>? Media = null);

/// <summary>
/// 列表/搜索分页响应：total 为过滤后（分页前）总数。
/// </summary>
public record ContentListResponse(
    List<ContentResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed record RecipientsResult(List<RecipientResponse> Recipients, string? Error);

/// <summary>
/// MediaAsset → 响应投影的单一出口，供上传响应与内容响应共用。
/// </summary>
public static class MediaResponses
{
    public static MediaResponse ToResponse(MediaAsset asset)
    {
        return new MediaResponse(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            $"/media/{asset.StoredName}",
            asset.ThumbStoredName is null ? null : $"/media/{asset.ThumbStoredName}",
            asset.SizeBytes);
    }
}
