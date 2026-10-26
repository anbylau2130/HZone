using System.Security.Claims;
using System.Security.Cryptography;
using HZone.Api.Authentication;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 只读分享外链：作者为内容签发匿名只读凭证（给未注册的家人看）。
/// 安全设计：token 256-bit 随机（base64url 43 字符）、只存 SHA-256 哈希
///（明文仅创建响应返回一次，PAT 同模式）、≤30 天过期、可撤销、内容删除级联；
/// 公开端点匿名只读，不暴露作者邮箱等任何账户信息（仅昵称）。
/// </summary>
public static class ShareLinkEndpoints
{
    private const int MaxDays = 30;

    public static void MapShareLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // 创建（仅作者）：days 1-30 默认 7；明文 token 只在本次响应出现。
        app.MapPost("/contents/{id:guid}/share-links", [Authorize] async (
            Guid id,
            CreateShareLinkRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id && c.AuthorId == me, ct);
            if (content is null)
            {
                return Results.NotFound(new { error = "content not found" });
            }
            var days = request.Days ?? 7;
            if (days < 1 || days > MaxDays)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["days"] = new[] { $"days must be 1-{MaxDays}" },
                });
            }

            var token = Base64UrlToken();
            var link = new ShareLink
            {
                Id = Guid.NewGuid(),
                ContentId = id,
                OwnerUserId = me,
                TokenHash = PatToken.Sha256Hex(token),
                ExpiresAt = DateTime.UtcNow.AddDays(days),
                CreatedAt = DateTime.UtcNow,
            };
            db.ShareLinks.Add(link);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/share-links/{link.Id}", new
            {
                link.Id,
                token,
                expiresAt = link.ExpiresAt,
                url = $"/#/pages/share/share?token={token}",
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 我的分享列表（作者视角；绝不含 token/tokenHash）。
        app.MapGet("/me/share-links", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var items = await db.ShareLinks
                .Where(s => s.OwnerUserId == me)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    s.ContentId,
                    ContentTitle = db.Contents.OfType<Journal>()
                        .Where(j => j.Id == s.ContentId)
                        .Select(j => j.Title)
                    .FirstOrDefault(),
                    s.ExpiresAt,
                    s.CreatedAt,
                    s.RevokedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 撤销（仅本人；幂等）。
        app.MapDelete("/share-links/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var link = await db.ShareLinks.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == me, ct);
            if (link is null)
            {
                return Results.NotFound(new { error = "share link not found" });
            }
            if (link.RevokedAt is null)
            {
                link.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 公开只读：匿名凭 token 读内容（不含邮箱/收件人/媒体鉴权路径）。
        app.MapGet("/public/shared/{token}", async (
            string token,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var payload = await FindLiveByTokenAsync(db, token, ct);
            if (payload is null)
            {
                return Results.NotFound(new { error = "not found or expired" });
            }
            return Results.Ok(payload);
        });

        // 公开媒体：token 关联内容的媒体直出（Range 支持预览；校验媒体确属该内容）。
        app.MapGet("/public/shared/{token}/media/{name}", async (
            string token,
            string name,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            if (!MediaEndpoints.IsValidMediaName(name))
            {
                return Results.BadRequest(new { error = "invalid media name" });
            }
            var storedName = name;
            var link = await db.ShareLinks
                .Include(s => s.Content)
                .FirstOrDefaultAsync(s => s.TokenHash == PatToken.Sha256Hex(token), ct);
            if (link is null || link.RevokedAt is not null || link.ExpiresAt < DateTime.UtcNow || link.Content is null)
            {
                return Results.NotFound(new { error = "not found or expired" });
            }
            var belongs = await db.MediaAssets
                .AnyAsync(m => m.StoredName == storedName
                    && ((m.ContentId == link.ContentId)
                        || (m.OwnerUserId == link.OwnerUserId && m.ContentId == null)), ct);
            if (!belongs)
            {
                return Results.NotFound(new { error = "not found" });
            }
            var asset = await db.MediaAssets.FirstAsync(m => m.StoredName == storedName, ct);
            try
            {
                var stream = await storage.OpenAsync(storedName);
                return Results.File(stream, asset.ContentType, enableRangeProcessing: true);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "not found" });
            }
        });
    }

    private static async Task<object?> FindLiveByTokenAsync(HZoneDbContext db, string token, CancellationToken ct)
    {
        var link = await db.ShareLinks
            .FirstOrDefaultAsync(s => s.TokenHash == PatToken.Sha256Hex(token), ct);
        if (link is null || link.RevokedAt is not null || link.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }
        var content = await db.Contents
            .FirstOrDefaultAsync(c => c.Id == link.ContentId, ct);
        if (content is null)
        {
            return null;
        }
        var author = await db.Users
            .Where(u => u.Id == content.AuthorId)
            .Select(u => u.Nickname)
            .FirstAsync(ct);
        var media = await db.MediaAssets
            .Where(m => m.ContentId == content.Id)
            .Select(m => new { m.StoredName, m.Kind, m.ContentType })
            .ToListAsync(ct);
        return new
        {
            kind = content is Say ? "say" : "journal",
            authorNickname = author,
            text = content is Say s ? s.Text : null,
            title = content is Journal j ? j.Title : null,
            markdownBody = content is Journal j2 ? j2.MarkdownBody : null,
            createdAt = content.CreatedAt,
            weather = content.WeatherCity is null ? null : new
            {
                city = content.WeatherCity,
                condition = content.WeatherCondition,
                tempC = content.WeatherTempC,
            },
            media = media.Select(m => new
            {
                url = $"/public/shared/{token}/media/{m.StoredName}",
                kind = m.Kind.ToString().ToLowerInvariant(),
            }).ToList(),
        };
    }

    /// <summary>256-bit 随机 base64url（43 字符）。</summary>
    private static string Base64UrlToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public sealed record CreateShareLinkRequest(int? Days);
