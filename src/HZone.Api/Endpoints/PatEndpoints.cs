using System.Security.Claims;
using HZone.Api.Authentication;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class PatEndpoints
{
    public const string MaxPerUserKey = "pat.maxPerUser";
    private const int DefaultMaxPerUser = 20;

    public static void MapPatEndpoints(this IEndpointRouteBuilder app)
    {
        // 生成 PAT：明文仅本次响应返回一次，落库只存 SHA-256 哈希；活跃 PAT 上限可配（默认 20，超限 409）。
        app.MapPost("/pats", [Authorize] async (
            CreatePatRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "name is required" },
                });
            }

            var max = await GetMaxPerUserAsync(db, ct);
            var activeCount = await db.PersonalAccessTokens
                .CountAsync(p => p.UserId == userId && p.RevokedAt == null, ct);
            if (activeCount >= max)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "PAT limit reached");
            }

            var plainToken = PatToken.Generate();
            var pat = new PersonalAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                TokenHash = PatToken.Sha256Hex(plainToken),
                CreatedAt = DateTime.UtcNow,
            };
            db.PersonalAccessTokens.Add(pat);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/pats/{pat.Id}", new
            {
                id = pat.Id,
                name = pat.Name,
                token = plainToken,
                createdAt = pat.CreatedAt,
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 列表：仅本人 PAT 元数据，绝不含 token/tokenHash。
        app.MapGet("/pats", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var items = await db.PersonalAccessTokens
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.CreatedAt,
                    p.RevokedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { items, total = items.Count });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 吊销：仅本人 PAT 可吊销，非本人/不存在统一 404；重复吊销幂等 200。
        app.MapDelete("/pats/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var pat = await db.PersonalAccessTokens
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (pat is null)
            {
                return Results.NotFound(new { error = "PAT not found" });
            }

            if (pat.RevokedAt is null)
            {
                pat.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static async Task<int> GetMaxPerUserAsync(HZoneDbContext db, CancellationToken ct)
    {
        var value = await db.SystemConfigs
            .Where(c => c.Key == MaxPerUserKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        return int.TryParse(value, out var max) ? max : DefaultMaxPerUser;
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public sealed record CreatePatRequest(string Name);
