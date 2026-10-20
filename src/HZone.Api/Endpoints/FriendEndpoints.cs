using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class FriendEndpoints
{
    public static void MapFriendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/friends", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var rows = await db.Friendships
                .Where(f => f.Status == FriendshipStatus.Accepted &&
                    (f.RequesterId == userId || f.AddresseeId == userId))
                .Select(f => new { f.RequesterId, f.AddresseeId })
                .ToListAsync(ct);
            var friendIds = rows
                .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
                .ToList();

            var friends = (await db.Users
                .Where(u => friendIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Nickname, u.Email, u.AvatarUrl })
                .ToListAsync(ct))
                .Select(u => new { u.Id, u.Nickname, u.Email, AvatarUrl = AuthEndpoints.MediaUrl(u.AvatarUrl) })
                .ToList();

            return Results.Ok(friends);
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/friends/requests", [Authorize] async (
            SendFriendRequestRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var requesterId = CurrentUserId(principal);
            if (request.TargetUserId == requesterId)
            {
                return Results.BadRequest(new { error = "cannot add yourself" });
            }

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == request.TargetUserId, ct);
            if (target is null || target.IsDisabled)
            {
                return Results.NotFound(new { error = "target user not found" });
            }

            var existing = await db.Friendships.FirstOrDefaultAsync(f =>
                (f.RequesterId == requesterId && f.AddresseeId == request.TargetUserId) ||
                (f.RequesterId == request.TargetUserId && f.AddresseeId == requesterId), ct);
            if (existing is not null)
            {
                return Results.Conflict(new { error = "friendship already exists" });
            }

            var now = DateTime.UtcNow;
            var friendship = new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = requesterId,
                AddresseeId = target.Id,
                Status = FriendshipStatus.Pending,
                CreatedAt = now,
            };
            db.Friendships.Add(friendship);
            db.InboxMessages.Add(new InboxMessage
            {
                Id = Guid.NewGuid(),
                UserId = target.Id,
                Type = InboxMessageType.FriendRequest,
                ContentId = null,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/friends/requests/{friendship.Id}", new
            {
                id = friendship.Id,
                addressee = new { id = target.Id, nickname = target.Nickname },
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapGet("/friends/requests", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var pending = await db.Friendships
                .Where(f => f.Status == FriendshipStatus.Pending &&
                    (f.AddresseeId == userId || f.RequesterId == userId))
                .Select(f => new { f.Id, f.RequesterId, f.AddresseeId })
                .ToListAsync(ct);
            var counterpartyIds = pending
                .Select(f => f.AddresseeId == userId ? f.RequesterId : f.AddresseeId)
                .ToList();
            var users = await db.Users
                .Where(u => counterpartyIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Nickname })
                .ToListAsync(ct);

            var incoming = pending
                .Where(f => f.AddresseeId == userId)
                .Select(f => new
                {
                    f.Id,
                    requester = users.Single(u => u.Id == f.RequesterId),
                })
                .ToList();
            var outgoing = pending
                .Where(f => f.RequesterId == userId)
                .Select(f => new
                {
                    f.Id,
                    addressee = users.Single(u => u.Id == f.AddresseeId),
                })
                .ToList();

            return Results.Ok(new { incoming, outgoing });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/friends/requests/{id:guid}/accept", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var friendship = await db.Friendships.FirstOrDefaultAsync(
                f => f.Id == id && f.Status == FriendshipStatus.Pending, ct);
            if (friendship is null || friendship.AddresseeId != userId)
            {
                return Results.NotFound(new { error = "friend request not found" });
            }

            friendship.Status = FriendshipStatus.Accepted;
            friendship.AcceptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/friends/requests/{id:guid}/decline", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var friendship = await db.Friendships.FirstOrDefaultAsync(
                f => f.Id == id && f.Status == FriendshipStatus.Pending, ct);
            if (friendship is null || friendship.AddresseeId != userId)
            {
                return Results.NotFound(new { error = "friend request not found" });
            }

            db.Friendships.Remove(friendship);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapDelete("/friends/{userId:guid}", [Authorize] async (
            Guid userId,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var currentUserId = CurrentUserId(principal);
            var rows = await db.Friendships
                .Where(f => (f.RequesterId == currentUserId && f.AddresseeId == userId) ||
                    (f.RequesterId == userId && f.AddresseeId == currentUserId))
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                db.Friendships.Remove(row);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
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

public sealed record SendFriendRequestRequest(Guid TargetUserId);
