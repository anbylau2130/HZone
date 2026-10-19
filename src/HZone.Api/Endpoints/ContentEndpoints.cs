using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class ContentEndpoints
{
    private static readonly Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>>
        RequireInitialized = async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
            if (!await SetupState.IsInitializedAsync(db, context.HttpContext.RequestAborted))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "instance not initialized");
            }

            return await next(context);
        };

    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/contents/says", [Authorize] async (
            CreateSayRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var recipientsResult = await ValidateRecipientsAsync(db, authorId, request.RecipientIds, ct);
            if (recipientsResult.Error is not null)
            {
                return Results.BadRequest(new { error = recipientsResult.Error });
            }

            var now = DateTime.UtcNow;
            var say = new Say
            {
                Id = Guid.NewGuid(),
                AuthorId = authorId,
                Text = request.Text,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // 绑定媒体（ticket 06）：校验存在/属主/未绑定 + 数量与类型规则。
            var mediaError = await BindMediaAsync(db, authorId, say.Id, request.MediaIds, ct);
            if (mediaError is not null)
            {
                return Results.BadRequest(new { error = mediaError });
            }

            foreach (var recipient in recipientsResult.Recipients)
            {
                db.ContentRecipients.Add(new ContentRecipient
                {
                    Id = Guid.NewGuid(),
                    ContentId = say.Id,
                    RecipientUserId = recipient.UserId,
                    AddedAt = now,
                });
            }

            db.Contents.Add(say);
            await db.SaveChangesAsync(ct);

            var media = await LoadMediaAsync(db, say.Id, ct);
            return Results.Created($"/contents/{say.Id}", new ContentResponse(
                say.Id,
                "say",
                say.AuthorId,
                say.Text,
                null,
                null,
                recipientsResult.Recipients,
                say.CreatedAt,
                media));
        }).AddEndpointFilter(RequireInitialized);

        app.MapPost("/contents/journals", [Authorize] async (
            CreateJournalRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var recipientsResult = await ValidateRecipientsAsync(db, authorId, request.RecipientIds, ct);
            if (recipientsResult.Error is not null)
            {
                return Results.BadRequest(new { error = recipientsResult.Error });
            }

            var now = DateTime.UtcNow;
            var journal = new Journal
            {
                Id = Guid.NewGuid(),
                AuthorId = authorId,
                Title = request.Title,
                MarkdownBody = request.Markdown,
                CreatedAt = now,
                UpdatedAt = now,
            };

            foreach (var recipient in recipientsResult.Recipients)
            {
                db.ContentRecipients.Add(new ContentRecipient
                {
                    Id = Guid.NewGuid(),
                    ContentId = journal.Id,
                    RecipientUserId = recipient.UserId,
                    AddedAt = now,
                });
            }

            db.Contents.Add(journal);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/contents/{journal.Id}", new ContentResponse(
                journal.Id,
                "journal",
                journal.AuthorId,
                null,
                journal.Title,
                journal.MarkdownBody,
                recipientsResult.Recipients,
                journal.CreatedAt,
                new List<MediaResponse>()));
        }).AddEndpointFilter(RequireInitialized);

        app.MapGet("/contents/{id:guid}", [Authorize] async (
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

            var recipients = (await db.ContentRecipients
                .Where(r => r.ContentId == id)
                .Select(r => new { r.RecipientUserId, r.User.Nickname })
                .ToListAsync(ct))
                .Select(r => new RecipientResponse(r.RecipientUserId, r.Nickname))
                .ToList();

            var media = await LoadMediaAsync(db, id, ct);
            return Results.Ok(ToResponse(content, recipients, media));
        });

        app.MapPut("/contents/{id:guid}", [Authorize] async (
            Guid id,
            UpdateContentRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IContentQuery contentQuery,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (content is null || content.AuthorId != authorId)
            {
                // 非作者与不存在统一 404，不泄露存在性。
                return Results.NotFound(new { error = "content not found" });
            }

            var changed = false;

            if (content is Say && request.MediaIds is not null)
            {
                // 说说的媒体重绑：先解绑旧媒体（文件保留），再按创建同规则绑定。
                var bound = await db.MediaAssets.Where(m => m.ContentId == id).ToListAsync(ct);
                foreach (var asset in bound)
                {
                    asset.ContentId = null;
                }

                var mediaError = await BindMediaAsync(db, authorId, id, request.MediaIds, ct);
                if (mediaError is not null)
                {
                    return Results.BadRequest(new { error = mediaError });
                }

                changed = true;
            }

            if (content is Say say && request.Text is not null)
            {
                say.Text = request.Text;
                changed = true;
            }

            if (content is Journal journal)
            {
                if (request.Title is not null)
                {
                    journal.Title = request.Title;
                    changed = true;
                }

                if (request.MarkdownBody is not null)
                {
                    journal.MarkdownBody = request.MarkdownBody;
                    changed = true;
                }
            }

            if (changed)
            {
                content.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            var recipients = await LoadRecipientsAsync(db, id, ct);
            var media = await LoadMediaAsync(db, id, ct);
            return Results.Ok(ToResponse(content, recipients, media));
        });

        app.MapDelete("/contents/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (content is null || content.AuthorId != authorId)
            {
                return Results.NotFound(new { error = "content not found" });
            }

            // 级联清理在应用层：媒体文件在磁盘，必须先删行再删文件（PRD 9.4）。
            var assets = await db.MediaAssets.Where(m => m.ContentId == id).ToListAsync(ct);
            db.MediaAssets.RemoveRange(assets);
            db.Contents.Remove(content);
            // DB 级联：ContentRecipient / Comment / InboxMessage（FK 已配 Cascade）。
            await db.SaveChangesAsync(ct);

            foreach (var asset in assets)
            {
                await storage.DeleteAsync(asset.StoredName);
                if (asset.ThumbStoredName is not null)
                {
                    await storage.DeleteAsync(asset.ThumbStoredName);
                }
            }

            return Results.NoContent();
        });
    }

    private static ContentResponse ToResponse(Content content, List<RecipientResponse> recipients, List<MediaResponse> media)
    {
        return content switch
        {
            Say s => new ContentResponse(s.Id, "say", s.AuthorId, s.Text, null, null, recipients, s.CreatedAt, media),
            Journal j => new ContentResponse(j.Id, "journal", j.AuthorId, null, j.Title, j.MarkdownBody, recipients, j.CreatedAt, media),
            _ => throw new InvalidOperationException("unknown content type"),
        };
    }

    private static async Task<List<RecipientResponse>> LoadRecipientsAsync(
        HZoneDbContext db, Guid contentId, CancellationToken ct)
    {
        return (await db.ContentRecipients
                .Where(r => r.ContentId == contentId)
                .Select(r => new { r.RecipientUserId, r.User.Nickname })
                .ToListAsync(ct))
            .Select(r => new RecipientResponse(r.RecipientUserId, r.Nickname))
            .ToList();
    }

    private static async Task<List<MediaResponse>> LoadMediaAsync(
        HZoneDbContext db, Guid contentId, CancellationToken ct)
    {
        return (await db.MediaAssets
                .Where(m => m.ContentId == contentId)
                .ToListAsync(ct))
            .Select(MediaResponses.ToResponse)
            .ToList();
    }

    /// <summary>
    /// 校验并绑定媒体到内容：媒体须存在、属当前用户、未绑定；
    /// 数量与类型规则 = ≤9 张图 或 1 个视频/音频。返回错误消息或 null。
    /// </summary>
    private static async Task<string?> BindMediaAsync(
        HZoneDbContext db, Guid authorId, Guid contentId, List<Guid>? mediaIds, CancellationToken ct)
    {
        var ids = (mediaIds ?? new List<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var assets = await db.MediaAssets
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);
        if (assets.Count != ids.Count
            || assets.Any(m => m.OwnerUserId != authorId)
            || assets.Any(m => m.ContentId is not null))
        {
            return "invalid media ids";
        }

        var validShape = (assets.Count <= 9 && assets.All(m => m.Kind == MediaKind.Image))
            || (assets.Count == 1 && assets[0].Kind != MediaKind.Image);
        if (!validShape)
        {
            return "media count or kind violates say rules";
        }

        foreach (var asset in assets)
        {
            asset.ContentId = contentId;
        }

        return null;
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }

    private static async Task<RecipientsResult> ValidateRecipientsAsync(
        HZoneDbContext db,
        Guid authorId,
        List<Guid> recipientIds,
        CancellationToken ct)
    {
        var ids = (recipientIds ?? new List<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new RecipientsResult(new List<RecipientResponse>(), null);
        }

        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nickname })
            .ToListAsync(ct);
        if (users.Count != ids.Count)
        {
            return new RecipientsResult(new List<RecipientResponse>(), "recipient user not found");
        }

        var friendships = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Where(f => f.RequesterId == authorId || f.AddresseeId == authorId)
            .Select(f => new { f.RequesterId, f.AddresseeId })
            .ToListAsync(ct);
        var friendIds = friendships
            .Select(f => f.RequesterId == authorId ? f.AddresseeId : f.RequesterId)
            .ToList();
        if (ids.Any(id => !friendIds.Contains(id)))
        {
            return new RecipientsResult(new List<RecipientResponse>(), "recipient is not an accepted friend");
        }

        var recipients = users
            .Select(u => new RecipientResponse(u.Id, u.Nickname))
            .ToList();
        return new RecipientsResult(recipients, null);
    }
}
