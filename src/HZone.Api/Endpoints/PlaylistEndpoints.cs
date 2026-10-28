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
/// 另含服务器音乐库：admin 配置一个磁盘目录，目录内音频文件作为所有用户播放器的默认列表
///（虚拟项，与用户项合并展示；配置键 player.musicDir，文件经 /player/file/{name} 鉴权流式下发）。
/// </summary>
public static class PlaylistEndpoints
{
    private const int MaxItemsPerUser = 100;
    public const string MusicDirKey = "player.musicDir";
    private static readonly string[] ServerAudioExtensions =
        [".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".oga", ".opus"];

    public static void MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- 服务器音乐库 ----------
        // 目录列表：所有登录用户可见（admin 配置、全员共享的默认曲库）；按文件名排序，仅顶层文件。
        app.MapGet("/me/player/server-library", [Authorize] async (
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var dir = await db.SystemConfigs.AsNoTracking()
                .Where(c => c.Key == MusicDirKey)
                .Select(c => c.Value)
                .FirstOrDefaultAsync(ct);
            var lib = ListServerAudio(dir);
            return Results.Ok(new
            {
                musicDir = lib is null ? null : dir,
                files = lib?.Files.ToList() ?? [],
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 目录配置（仅 admin）：路径必须已存在（空串 = 清除，库停用）。
        app.MapPut("/player/music-dir", [Authorize] async (
            SetMusicDirRequest request,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var dir = request.MusicDir?.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                var clear = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == MusicDirKey, ct);
                if (clear is not null)
                {
                    db.SystemConfigs.Remove(clear);
                    await db.SaveChangesAsync(ct);
                }
                return Results.Ok(new { musicDir = (string?)null });
            }
            if (!Directory.Exists(dir))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["musicDir"] = new[] { "directory does not exist" },
                });
            }
            var existing = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == MusicDirKey, ct);
            if (existing is null)
            {
                db.SystemConfigs.Add(new SystemConfig { Key = MusicDirKey, Value = dir });
            }
            else
            {
                existing.Value = dir;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { musicDir = dir });
        }).AddEndpointFilter(SetupState.RequireAdmin);

        // 文件下发：name 必须是纯文件名（拒绝路径穿越），仅限配置目录顶层，Range 支持（进度条 seek）。
        app.MapGet("/player/file/{name}", [Authorize] async (
            string name,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            if (Path.GetFileName(name) != name || name.Contains(".."))
            {
                return Results.BadRequest(new { error = "invalid file name" });
            }
            var dir = await db.SystemConfigs.AsNoTracking()
                .Where(c => c.Key == MusicDirKey)
                .Select(c => c.Value)
                .FirstOrDefaultAsync(ct);
            var files = ListServerAudio(dir);
            if (files is null || !files.Value.Files.Any(f => f.Name == name))
            {
                return Results.NotFound(new { error = "file not found" });
            }
            var fullPath = Path.Combine(files.Value.Dir, name);
            return Results.File(fullPath, ContentTypeFor(name), enableRangeProcessing: true);
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 账号播放列表 ----------
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
                // 同一资产不重复入列（同一文件上传幂等后是同一资产，列表只留一条）
                var dup = await db.PlaylistItems
                    .AnyAsync(p => p.UserId == userId && p.MediaAssetId == assetId, ct);
                if (dup)
                {
                    return Results.Conflict(new { error = "already in playlist" });
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

        // 重命名：仅本人条目（歌名显示与资产名解耦——添加后即为独立快照）。
        app.MapPut("/me/playlist/{id:guid}/title", [Authorize] async (
            Guid id,
            RenamePlaylistItemRequest request,
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
            var title = request.Title?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = new[] { "title required (max 200 chars)" },
                });
            }
            item.Title = title;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { item.Id, item.Title });
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

    /// <summary>目录音频清单（顶层、按文件名排序）；null = 未配置目录（库停用）。</summary>
    private static (string Dir, IEnumerable<ServerAudioFile> Files)? ListServerAudio(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }
        var files = Directory.EnumerateFiles(dir)
            .Where(p => ServerAudioExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ServerAudioFile(Path.GetFileName(p), new FileInfo(p).Length))
            .ToList();
        return (dir, files);
    }

    private static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".ogg" or ".oga" => "audio/ogg",
        ".opus" => "audio/opus",
        _ => "application/octet-stream",
    };

    public sealed record ServerAudioFile(string Name, long SizeBytes);
}

public sealed record AddPlaylistItemRequest(string SourceKind, string? Title, string? Url, Guid? MediaAssetId);

public sealed record ReorderPlaylistRequest(List<Guid> Ids);

public sealed record SetMusicDirRequest(string? MusicDir);

public sealed record RenamePlaylistItemRequest(string? Title);
