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
    private const int MaxPlaylistsPerUser = 20;
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

        // ---------- 歌单（多歌单）：列表/新建/改名/删除 ----------
        // 列表：本人歌单按创建序（默认歌单 CreatedAt 压最小恒排前），含条目数；
        // 一张都没有时惰性建「默认歌单」（新用户/删光后），并把游离孤儿条目挂上。
        app.MapGet("/me/playlists", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            await EnsureDefaultPlaylistAsync(db, userId, ct);
            var playlists = await db.Playlists.AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, ItemCount = p.Items.Count, p.CreatedAt })
                .ToListAsync(ct);
            return Results.Ok(new { items = playlists });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 新建：名称 1~50 字符；每人上限 20 张。
        app.MapPost("/me/playlists", [Authorize] async (
            CreatePlaylistRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 50)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "name is required (max 50 chars)" },
                });
            }
            if (await db.Playlists.CountAsync(p => p.UserId == userId, ct) >= MaxPlaylistsPerUser)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "playlist limit reached");
            }
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                CreatedAt = DateTime.UtcNow,
            };
            db.Playlists.Add(playlist);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/playlists/{playlist.Id}",
                new { playlist.Id, playlist.Name, ItemCount = 0, playlist.CreatedAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 改名：仅本人；非本人/不存在统一 404。
        app.MapPut("/me/playlists/{id:guid}", [Authorize] async (
            Guid id,
            CreatePlaylistRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var playlist = await db.Playlists
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (playlist is null)
            {
                return Results.NotFound(new { error = "playlist not found" });
            }
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 50)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "name is required (max 50 chars)" },
                });
            }
            playlist.Name = name;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { playlist.Id, playlist.Name });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 删除：仅本人；条目随删（级联）；删光后下次 GET 惰性重建默认歌单。
        app.MapDelete("/me/playlists/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var playlist = await db.Playlists
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (playlist is null)
            {
                return Results.NotFound(new { error = "playlist not found" });
            }
            // 手工级联（存量 dev 库的迁移路径无 FK 约束，新库有约束也兼容）
            await db.PlaylistItems.Where(p => p.PlaylistId == id).ExecuteDeleteAsync(ct);
            db.Playlists.Remove(playlist);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 账号播放列表（单张歌单内容） ----------
        // 列表：按 SortOrder 升序；playlistId 缺省 = 第一张（默认歌单）；
        // media 项附带 storedName 供前端拼 /media 播放路径。
        app.MapGet("/me/playlist", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            Guid? playlistId,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var playlist = await ResolvePlaylistAsync(db, userId, playlistId, ct);
            if (playlist is null)
            {
                return Results.NotFound(new { error = "playlist not found" });
            }
            var items = await db.PlaylistItems
                .Where(p => p.UserId == userId && p.PlaylistId == playlist.Id)
                .OrderBy(p => p.SortOrder)
                .Select(p => new
                {
                    p.Id,
                    p.SourceKind,
                    p.Title,
                    p.Url,
                    p.MediaAssetId,
                    MediaStoredName = p.MediaAsset != null ? p.MediaAsset.StoredName : null,
                    // 音频元数据（media 项来自资产；url 项为 null）：列表行展示用
                    Artist = p.MediaAsset != null ? p.MediaAsset.Artist : null,
                    DurationSeconds = p.MediaAsset != null ? p.MediaAsset.DurationSeconds : null,
                    BitrateKbps = p.MediaAsset != null ? p.MediaAsset.BitrateKbps : null,
                    SizeBytes = p.MediaAsset != null ? (long?)p.MediaAsset.SizeBytes : null,
                    OriginalFileName = p.MediaAsset != null ? p.MediaAsset.OriginalName : null,
                    // 歌词原文（lrc；无则 null）：播放器解析滚动
                    Lyrics = p.Lyrics,
                    p.PlaylistId,
                    p.SortOrder,
                    p.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { playlistId = playlist.Id, playlistName = playlist.Name, items, total = items.Count });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 添加：url 项必须有 http/https 直链与标题；media 项必须引用本人 audio 资产，标题缺省用资产原名。
        // playlistId 缺省 = 第一张（默认歌单）；上限/去重均按目标歌单计。
        app.MapPost("/me/playlist", [Authorize] async (
            AddPlaylistItemRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var playlist = await ResolvePlaylistAsync(db, userId, request.PlaylistId, ct);
            if (playlist is null)
            {
                return Results.NotFound(new { error = "playlist not found" });
            }
            if (await db.PlaylistItems.CountAsync(
                    p => p.UserId == userId && p.PlaylistId == playlist.Id, ct) >= MaxItemsPerUser)
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
                // 同一资产同歌单不重复入列（同一文件上传幂等后是同一资产；跨歌单允许同曲）
                var dup = await db.PlaylistItems
                    .AnyAsync(p => p.UserId == userId && p.PlaylistId == playlist.Id
                        && p.MediaAssetId == assetId, ct);
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
                .Where(p => p.UserId == userId && p.PlaylistId == playlist.Id)
                .MaxAsync(p => (int?)p.SortOrder, ct);
            var item = new PlaylistItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlaylistId = playlist.Id,
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

        // 歌词录入：lrc 原文整体存取（空串 = 清除）；仅本人条目。上限防滥用。
        app.MapPut("/me/playlist/{id:guid}/lyrics", [Authorize] async (
            Guid id,
            SetLyricsRequest request,
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
            var lyrics = request.Lyrics?.Trim();
            if (lyrics?.Length == 0) lyrics = null;
            if (lyrics is not null && lyrics.Length > 100_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lyrics"] = new[] { "lyrics too long (max 100000 chars)" },
                });
            }
            item.Lyrics = lyrics;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { item.Id, hasLyrics = lyrics is not null });
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

    /// <summary>惰性兜底：一张歌单都没有（新用户/删光）时建「默认歌单」。</summary>
    private static async Task EnsureDefaultPlaylistAsync(HZoneDbContext db, Guid userId, CancellationToken ct)
    {
        if (await db.Playlists.AnyAsync(p => p.UserId == userId, ct))
        {
            return;
        }
        db.Playlists.Add(new Playlist
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "默认歌单",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 解析目标歌单：显式 id 必须是本人的（否则 null，端点回 404 防越权探测）；
    /// 缺省 = 第一张（无歌单则惰性建默认）。
    /// </summary>
    private static async Task<Playlist?> ResolvePlaylistAsync(
        HZoneDbContext db, Guid userId, Guid? playlistId, CancellationToken ct)
    {
        if (playlistId is { } id)
        {
            var found = await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
            if (found is not null)
            {
                return found;
            }
            // 显式 id 失效（歌单被删/换端残留 localStorage）：回落默认歌单而非 404——
            // 取曲/加曲是用户操作，不应因 stale id 硬失败（曾致前端 toast「playlist not found」）
        }
        await EnsureDefaultPlaylistAsync(db, userId, ct);
        return await db.Playlists
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Name)
            .FirstAsync(ct);
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

public sealed record AddPlaylistItemRequest(
    string SourceKind,
    string? Title,
    string? Url,
    Guid? MediaAssetId,
    Guid? PlaylistId = null);

public sealed record CreatePlaylistRequest(string? Name);

public sealed record ReorderPlaylistRequest(List<Guid> Ids);

public sealed record SetMusicDirRequest(string? MusicDir);

public sealed record RenamePlaylistItemRequest(string? Title);

public sealed record SetLyricsRequest(string? Lyrics);
