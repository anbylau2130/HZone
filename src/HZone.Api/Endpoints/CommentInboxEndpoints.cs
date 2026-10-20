using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class CommentEndpoints
{
    private const int MaxCommentLength = 2000;

    public static void MapCommentEndpoints(this IEndpointRouteBuilder app)
    {
        // 可见名单成员可评论；非名单成员与不存在统一 404，防存在性泄漏。
        app.MapPost("/contents/{id:guid}/comments", [Authorize] async (
            Guid id,
            CreateCommentRequest request,
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var text = request.Text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxCommentLength)
            {
                return Results.BadRequest(new { error = "text is required and must be at most 2000 characters" });
            }

            var content = await contentQuery.FindVisibleAsync(id, authorId, ct);
            if (content is null)
            {
                return Results.NotFound(new { error = "content not found" });
            }

            var now = DateTime.UtcNow;
            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                ContentId = id,
                AuthorId = authorId,
                Text = text,
                CreatedAt = now,
            };
            db.Comments.Add(comment);

            // 站内信给内容作者；自己评论自己不产生（防自骚扰）。
            if (authorId != content.AuthorId)
            {
                db.InboxMessages.Add(new InboxMessage
                {
                    Id = Guid.NewGuid(),
                    UserId = content.AuthorId,
                    Type = InboxMessageType.Commented,
                    ContentId = id,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync(ct);
            var author = await db.Users
                .Where(u => u.Id == authorId)
                .Select(u => new { u.Id, u.Nickname, u.AvatarUrl })
                .SingleAsync(ct);
            return Results.Created(
                $"/contents/{id}/comments/{comment.Id}",
                new CommentResponse(comment.Id, id,
                    AuthorResponse.Of(author.Id, author.Nickname, author.AvatarUrl), text, now));
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 评论列表：同样仅可见名单内可读，时间正序。
        app.MapGet("/contents/{id:guid}/comments", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var viewerId = CurrentUserId(principal);
            var content = await contentQuery.FindVisibleAsync(id, viewerId, ct);
            if (content is null)
            {
                return Results.NotFound(new { error = "content not found" });
            }

            var items = await db.Comments
                .Where(c => c.ContentId == id)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CommentResponse(
                    c.Id,
                    c.ContentId,
                    new AuthorResponse(c.AuthorId, c.Author.Nickname, c.Author.AvatarUrl == null
                        ? null
                        : "/media/" + c.Author.AvatarUrl),
                    c.Text,
                    c.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(new CommentListResponse(items, items.Count));
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public static class InboxEndpoints
{
    /// <summary>previewText 摘要长度上限（字符）。</summary>
    private const int PreviewLength = 50;

    public static void MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        // 我的站内信列表：时间倒序，条目含读时生成的 previewText（作者昵称+内容摘要）。
        app.MapGet("/inbox", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);

            var total = await db.InboxMessages.CountAsync(m => m.UserId == me, ct);
            var unreadCount = await db.InboxMessages
                .CountAsync(m => m.UserId == me && m.ReadAt == null, ct);
            var messages = await db.InboxMessages
                .Where(m => m.UserId == me)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(Math.Max(0, offset ?? 0))
                .Take(limit is null or <= 0 ? 20 : Math.Min(limit.Value, 100))
                .ToListAsync(ct);

            var contentIds = messages
                .Where(m => m.ContentId is not null)
                .Select(m => m.ContentId!.Value)
                .Distinct()
                .ToList();

            // 读时批量装配 previewText：内容作者昵称 + 摘要（say 正文 / journal 标题，50 字符截断）。
            var contents = await db.Contents
                .Where(c => contentIds.Contains(c.Id))
                .ToListAsync(ct);
            var authorIds = contents.Select(c => c.AuthorId).Distinct().ToList();
            var nicknames = await db.Users
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);

            var items = messages
                .Select(m =>
                {
                    Guid? contentId = null;
                    string? contentKind = null;
                    string? previewText = null;
                    if (m.ContentId is { } contentMessageId)
                    {
                        var content = contents.Single(c => c.Id == contentMessageId);
                        contentId = content.Id;
                        contentKind = content is Say ? "say" : "journal";
                        var snippet = content is Say say ? say.Text : ((Journal)content).Title;
                        previewText = $"{nicknames[content.AuthorId]}：{Truncate(snippet, PreviewLength)}";
                    }

                    return new InboxMessageResponse(
                        m.Id,
                        m.Type switch
                        {
                            InboxMessageType.Mentioned => "mentioned",
                            InboxMessageType.Commented => "commented",
                            _ => "friendRequest",
                        },
                        contentId,
                        contentKind,
                        previewText,
                        m.CreatedAt,
                        m.ReadAt);
                })
                .ToList();

            return Results.Ok(new InboxListResponse(items, unreadCount, total));
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 红点计数：我的未读站内信条数。
        app.MapGet("/inbox/unread-count", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var unreadCount = await db.InboxMessages
                .CountAsync(m => m.UserId == me && m.ReadAt == null, ct);
            return Results.Ok(new { unreadCount });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 标记已读：只本人（非本人与不存在统一 404）；已读重复调用幂等 200。
        app.MapPost("/inbox/{id:guid}/read", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var message = await db.InboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (message is null || message.UserId != me)
            {
                return Results.NotFound(new { error = "inbox message not found" });
            }

            if (message.ReadAt is null)
            {
                message.ReadAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }

    /// <summary>previewText 摘要截断。</summary>
    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}

public sealed record CreateCommentRequest(string Text);

public sealed record CommentResponse(
    Guid Id,
    Guid ContentId,
    AuthorResponse Author,
    string Text,
    DateTime CreatedAt);

public sealed record CommentListResponse(List<CommentResponse> Items, int Total);

public sealed record InboxMessageResponse(
    Guid Id,
    string Type,
    Guid? ContentId,
    string? ContentKind,
    string? PreviewText,
    DateTime CreatedAt,
    DateTime? ReadAt);

public sealed record InboxListResponse(List<InboxMessageResponse> Items, int UnreadCount, int Total);
