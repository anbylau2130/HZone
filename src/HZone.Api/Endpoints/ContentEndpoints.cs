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

            return Results.Created($"/contents/{say.Id}", new ContentResponse(
                say.Id,
                "say",
                say.AuthorId,
                say.Text,
                null,
                null,
                recipientsResult.Recipients,
                say.CreatedAt));
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
                journal.CreatedAt));
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

            return Results.Ok(ToResponse(content, recipients));
        });
    }

    private static ContentResponse ToResponse(Content content, List<RecipientResponse> recipients)
    {
        var kind = content switch
        {
            Say say => "say",
            Journal journal => "journal",
            _ => throw new InvalidOperationException("unknown content type"),
        };

        return content switch
        {
            Say s => new ContentResponse(s.Id, kind, s.AuthorId, s.Text, null, null, recipients, s.CreatedAt),
            Journal j => new ContentResponse(j.Id, kind, j.AuthorId, null, j.Title, j.MarkdownBody, recipients, j.CreatedAt),
            _ => throw new InvalidOperationException("unknown content type"),
        };
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
