using System.Security.Claims;
using HZone.Api.Authentication;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 账号级播放列表（每用户一张）：双音源（media = 自己上传的音频资产 / url = 外部直链）。
/// 排序为端到端整体重排：PUT /me/playlist/reorder 传完整 id 序列，服务端按下标重编 SortOrder。
/// </summary>
public static class PlaylistEndpoints
{
    private const int MaxItemsPerUser = 100;

    public static void MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        // 列表：本人播放列表按 SortOrder 升序；media 项附带 storedName 供前端拼 /media 播放路径。
        app.MapGet("/me/playlist", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var items = await db.PlaylistItems
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.SortOrder)
                .Select(p => new
                {
                    p.Id,
                    p.SourceKind,
                    p.Title,
                    p.Url,
                    p.MediaAssetId,
                    MediaStoredName = p.MediaAsset != null ? p.MediaAsset.StoredName : null,
                    p.SortOrder,
                    p.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { items, total = items.Count });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 添加：url 项必须有 http/https 直链与标题；media 项必须引用本人 audio 资产，标题缺省用资产原名。
        app.MapPost("/me/playlist", [Authorize] async (
            AddPlaylistItemRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            if (await db.PlaylistItems.CountAsync(p => p.UserId == userId, ct) >= MaxItemsPerUser)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "playlist limit reached");
            }

            string title;
            string? url = null;
            Guid? mediaAssetId = null;
            if (request.SourceKind == "url")
            {
                url = request.Url?.Trim();
                if (string.IsNullOrEmpty(url)
                    || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    || url.Length > 2048)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["url"] = new[] { "a http(s) audio url is required" },
                    });
                }
                title = request.Title?.Trim() ?? "";
                if (title.Length == 0)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["title"] = new[] { "title is required for url items" },
                    });
                }
            }
            else if (request.SourceKind == "media")
            {
                if (request.MediaAssetId is not Guid assetId)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["mediaAssetId"] = new[] { "mediaAssetId is required for media items" },
                    });
                }
                var asset = await db.MediaAssets
                    .FirstOrDefaultAsync(m => m.Id == assetId && m.OwnerUserId == userId
                        && m.Kind == MediaKind.Audio, ct);
                if (asset is null)
                {
                    return Results.NotFound(new { error = "audio asset not found" });
                }
                mediaAssetId = assetId;
                title = string.IsNullOrWhiteSpace(request.Title) ? asset.OriginalName : request.Title.Trim();
            }
            else
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sourceKind"] = new[] { "sourceKind must be 'media' or 'url'" },
                });
            }
            if (title.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = new[] { "title too long (max 200)" },
                });
            }

            var maxOrder = await db.PlaylistItems
                .Where(p => p.UserId == userId)
                .MaxAsync(p => (int?)p.SortOrder, ct);
            var item = new PlaylistItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SourceKind = request.SourceKind,
                Title = title,
                Url = url,
                MediaAssetId = mediaAssetId,
                SortOrder = (maxOrder ?? 0) + 1,
                CreatedAt = DateTime.UtcNow,
            };
            db.PlaylistItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/playlist/{item.Id}", new
            {
                item.Id,
                item.SourceKind,
                item.Title,
                item.Url,
                item.MediaAssetId,
                item.SortOrder,
                item.CreatedAt,
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 整体重排：完整 id 序列（必须与本人现集合一致，多/少/重复 400），按下标重编 SortOrder。
        app.MapPut("/me/playlist/reorder", [Authorize] async (
            ReorderPlaylistRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var ids = request.Ids ?? [];
            if (ids.Count == 0 || ids.Distinct().Count() != ids.Count)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ids"] = new[] { "ids must be a non-empty permutation" },
                });
            }
            var items = await db.PlaylistItems
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            if (items.Count != ids.Count || items.All(i => ids.Contains(i.Id)) == false
                || ids.Any(id => items.All(i => i.Id != id)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ids"] = new[] { "ids must match the current playlist" },
                });
            }

            foreach (var item in items)
            {
                item.SortOrder = ids.IndexOf(item.Id) + 1;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 删除：仅本人条目，非本人/不存在统一 404。
        app.MapDelete("/me/playlist/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var item = await db.PlaylistItems
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (item is null)
            {
                return Results.NotFound(new { error = "playlist item not found" });
            }
            db.PlaylistItems.Remove(item);
            await db.SaveChangesAsync(ct);
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
}

public sealed record AddPlaylistItemRequest(string SourceKind, string? Title, string? Url, Guid? MediaAssetId);

public sealed record ReorderPlaylistRequest(List<Guid> Ids);
