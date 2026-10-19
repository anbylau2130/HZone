using System.Security.Claims;
using System.Security.Cryptography;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class InviteEndpoints
{
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int MaxActiveInvitesPerUser = 3;

    public static void MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/invites", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var now = DateTime.UtcNow;
            var activeCount = await db.InviteCodes.CountAsync(
                i => i.CreatedById == userId && i.UsedAt == null && i.ExpiresAt > now, ct);
            if (activeCount >= MaxActiveInvitesPerUser)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "invite quota exceeded");
            }

            string code;
            do
            {
                code = GenerateCode();
            }
            while (await db.InviteCodes.AnyAsync(i => i.Code == code, ct));

            var invite = new InviteCode
            {
                Id = Guid.NewGuid(),
                Code = code,
                CreatedById = userId,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7),
            };
            db.InviteCodes.Add(invite);
            await db.SaveChangesAsync(ct);

            return Results.Created("/invites", new { code = invite.Code, expiresAt = invite.ExpiresAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    // 8 位、无易混淆字符（0/O/1/I/L）
    private static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}
