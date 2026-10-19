using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HZone.Api.Endpoints;

public static class MediaEndpoints
{
    private const long DefaultImageLimit = 10L * 1024 * 1024;
    private const long DefaultVideoLimit = 200L * 1024 * 1024;
    private const long DefaultAudioLimit = 50L * 1024 * 1024;
    private const int MainImageMaxSide = 1920;
    private const int ThumbMaxSide = 320;
    private const int JpegQuality = 75;

    private static readonly System.Text.RegularExpressions.Regex ValidStoredName =
        new("^[A-Za-z0-9._-]{1,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/media", [Authorize] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart form with 'file' field required" });
            }

            // 通过 HttpRequest.ReadFormAsync 读取而非 IFormFile 参数绑定，避开 .NET 8 最小 API 的防伪令牌要求。
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null)
            {
                return Results.BadRequest(new { error = "multipart form with 'file' field required" });
            }

            var kind = ClassifyKind(file.ContentType);
            if (kind is null)
            {
                return Results.BadRequest(new { error = "unsupported content type" });
            }

            // 上限从 SystemConfig 读（media.maxImageBytes / media.maxVideoBytes / media.maxAudioBytes），无配置用默认值。
            var limit = await GetLimitAsync(db, kind.Value, ct);
            string storedName;
            string contentType = file.ContentType;
            string? thumbStoredName = null;
            long sizeBytes;

            if (kind == MediaKind.Image)
            {
                // 图片统一重编码：主图最长边 1920 JPEG q75 + 320px 缩略图；压缩后仍超限 → 413。
                byte[] mainBytes;
                byte[] thumbBytes;
                try
                {
                    (mainBytes, thumbBytes) = await CompressImageAsync(file.OpenReadStream(), ct);
                }
                catch (Exception ex) when (ex is SixLabors.ImageSharp.UnknownImageFormatException
                    or SixLabors.ImageSharp.InvalidImageContentException)
                {
                    // 解码失败 = 非图片数据，400。
                    return Results.BadRequest(new { error = "invalid image data" });
                }

                if (mainBytes.Length > limit)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "media too large");
                }

                await using var mainStream = new MemoryStream(mainBytes);
                await using var thumbStream = new MemoryStream(thumbBytes);
                storedName = await storage.SaveAsync(mainStream, file.FileName, "image/jpeg", ct);
                thumbStoredName = await storage.SaveAsync(thumbStream, file.FileName, "image/jpeg", ct);
                sizeBytes = mainBytes.Length;
                contentType = "image/jpeg";
            }
            else
            {
                // 视频/音频超限直接 413（ffmpeg 压缩决策留给 ticket 11）；不压缩直接存。
                if (file.Length > limit)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "media too large");
                }

                await using var raw = file.OpenReadStream();
                storedName = await storage.SaveAsync(raw, file.FileName, file.ContentType, ct);
                sizeBytes = file.Length;
            }

            // MediaAsset.ContentId 留空 = 临时媒体；被内容绑定后设置。
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                OwnerUserId = CurrentUserId(principal),
                StoredName = storedName,
                OriginalName = string.IsNullOrWhiteSpace(file.FileName) ? "upload" : file.FileName,
                Kind = kind.Value,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                ThumbStoredName = thumbStoredName,
                ContentId = null,
                CreatedAt = DateTime.UtcNow,
            };

            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/media/{asset.StoredName}", MediaResponses.ToResponse(asset));
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 缩略图与主文件走同一端点（缩略图 storedName 存在 MediaAsset.ThumbStoredName）。
        // 已知简化：不做每文件的可见名单校验（媒体 URL 不含授权参数，仅登录可用），PRD 允许。
        app.MapGet("/media/{storedName}", [Authorize] async (
            string storedName,
            IFileStorage storage,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            // 存储层对非法名抛 ArgumentException → 500，先校验给 404。
            if (!IsValidStoredName(storedName))
            {
                return Results.NotFound(new { error = "media not found" });
            }

            var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.StoredName == storedName, ct);
            string contentType;
            if (asset is not null)
            {
                contentType = asset.ContentType;
            }
            else
            {
                var thumbOwner = await db.MediaAssets.FirstOrDefaultAsync(m => m.ThumbStoredName == storedName, ct);
                if (thumbOwner is null)
                {
                    return Results.NotFound(new { error = "media not found" });
                }
                // 缩略图总是重编码为 jpeg。
                contentType = "image/jpeg";
            }

            try
            {
                // Results.File 在响应写完后负责 dispose，这里不能提前 await using。
                var stream = await storage.OpenAsync(storedName);
                return Results.File(stream, contentType);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "media not found" });
            }
            catch (DirectoryNotFoundException)
            {
                return Results.NotFound(new { error = "media not found" });
            }
        });
    }

    private static MediaKind? ClassifyKind(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return null;
        }

        // Content-Type 前缀判定 MediaKind。
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Image;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Video;
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Audio;
        return null;
    }

    private static async Task<(byte[] Main, byte[] Thumb)> CompressImageAsync(Stream input, CancellationToken ct)
    {
        // ImageSharp 解码需要可 seek 的流，先缓冲。
        await using var buffered = new MemoryStream();
        await input.CopyToAsync(buffered, ct);
        buffered.Position = 0;
        using var image = await Image.LoadAsync(buffered, ct);
        // ResizeMode.Max 不放大，只缩小。
        using var main = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(MainImageMaxSide, MainImageMaxSide),
        }));
        using var thumb = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(ThumbMaxSide, ThumbMaxSide),
        }));
        var mainMs = new MemoryStream();
        await main.SaveAsync(mainMs, new JpegEncoder { Quality = JpegQuality }, ct);
        var thumbMs = new MemoryStream();
        await thumb.SaveAsync(thumbMs, new JpegEncoder { Quality = JpegQuality }, ct);
        return (mainMs.ToArray(), thumbMs.ToArray());
    }

    private static bool IsValidStoredName(string storedName)
    {
        return ValidStoredName.IsMatch(storedName) && !storedName.Contains("..");
    }

    private static async Task<long> GetLimitAsync(HZoneDbContext db, MediaKind kind, CancellationToken ct)
    {
        var key = kind switch
        {
            MediaKind.Image => "media.maxImageBytes",
            MediaKind.Video => "media.maxVideoBytes",
            _ => "media.maxAudioBytes",
        };

        var value = await db.SystemConfigs
            .Where(c => c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) && limit > 0
            ? limit
            : kind switch
            {
                MediaKind.Image => DefaultImageLimit,
                MediaKind.Video => DefaultVideoLimit,
                _ => DefaultAudioLimit,
            };
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}
