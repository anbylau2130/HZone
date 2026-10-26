using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Core.Media;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using HZone.Api.Services;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HZone.Api.Endpoints;

public static class MediaEndpoints
{
    public const long DefaultImageLimit = 10L * 1024 * 1024;
    public const long DefaultVideoLimit = 200L * 1024 * 1024;
    public const long DefaultAudioLimit = 50L * 1024 * 1024;
    public const long DefaultFileLimit = 20L * 1024 * 1024;
    public const int DefaultVideoMaxSeconds = 300;
    private const int MainImageMaxSide = 1920;
    private const int ThumbMaxSide = 320;
    private const int JpegQuality = 75;

    // 附件扩展名白名单（插入文件功能）：不含可执行与 html/svg 等可内联执行的类型，
    // 下载时强制 attachment，避免用户上传的 HTML 同域内联造成存储型 XSS。
    private static readonly HashSet<string> FileExtensionAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".zip", ".txt", ".csv", ".md",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
    };

    private static readonly System.Text.RegularExpressions.Regex ValidStoredName =
        new("^[A-Za-z0-9._-]{1,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/media", [Authorize] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            IFFmpegCompressor ffmpeg,
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

            var kind = ClassifyKind(file.ContentType, file.FileName);
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
            else if (kind == MediaKind.File)
            {
                // 普通附件：原样存储（无转码/无缩略图），上限 media.maxFileBytes。
                if (file.Length > limit)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "media too large");
                }

                await using var raw = file.OpenReadStream();
                storedName = await storage.SaveAsync(raw, file.FileName, file.ContentType, ct);
                sizeBytes = file.Length;
            }
            else
            {
                // 视频/音频：超限且 ffmpeg 可用 → 单次转码（crf28/1280p、aac64k）；无 ffmpeg 或产物仍超限 → 413。
                var available = await ffmpeg.IsAvailableAsync(ct);
                var action = FFmpegCompressDecision.Decide(kind.Value, file.Length, limit, available);
                if (action == MediaCompressAction.Reject)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "media too large");
                }

                if (action == MediaCompressAction.FFmpegTranscode)
                {
                    await using var input = file.OpenReadStream();
                    using var buffered = new MemoryStream();
                    await input.CopyToAsync(buffered, ct);
                    var inputBytes = buffered.ToArray();
                    var transcoded = kind == MediaKind.Video
                        ? await ffmpeg.TranscodeVideoAsync(inputBytes, ct)
                        : await ffmpeg.TranscodeAudioAsync(inputBytes, ct);
                    var transcodedContentType = kind == MediaKind.Video ? "video/mp4" : "audio/mp4";
                    if (transcoded is null || transcoded.Length > limit)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                            title: "media too large");
                    }

                    await using var transcodedStream = new MemoryStream(transcoded);
                    storedName = await storage.SaveAsync(transcodedStream, file.FileName, transcodedContentType, ct);
                    contentType = transcodedContentType;
                    sizeBytes = transcoded.Length;
                }
                else
                {
                    await using var raw = file.OpenReadStream();
                    storedName = await storage.SaveAsync(raw, file.FileName, file.ContentType, ct);
                    sizeBytes = file.Length;
                }
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
                // 普通附件强制 attachment 下载：Content-Type 是客户端上传值，放任内联
                // 渲染（尤其 html/svg）会成为同域存储型 XSS 载体。
                try
                {
                    var stream = await storage.OpenAsync(storedName);
                    return asset.Kind == MediaKind.File
                        ? Results.File(stream, contentType, asset.OriginalName)
                        : Results.File(stream, contentType);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound(new { error = "media not found" });
                }
                catch (DirectoryNotFoundException)
                {
                    return Results.NotFound(new { error = "media not found" });
                }
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

        // 音频库（播放器）：本人音频资产列表，新→旧；播放列表「从音频库添加」用。
        app.MapGet("/me/media", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var items = await db.MediaAssets
                .Where(m => m.OwnerUserId == userId && m.Kind == MediaKind.Audio)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.OriginalName,
                    m.StoredName,
                    m.SizeBytes,
                    m.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { items, total = items.Count });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    internal static MediaKind? ClassifyKind(string? contentType, string? fileName = null)
    {
        // Content-Type 前缀判定媒体类。
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Image;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Video;
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Audio;
        }

        // 附件：按扩展名白名单判定（浏览器对无扩展名/未知类型文件可能给空或
        // application/octet-stream，扩展名是更可靠的意图信号）。
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (!string.IsNullOrEmpty(ext) && FileExtensionAllowlist.Contains(ext))
        {
            return MediaKind.File;
        }

        return null;
    }

    internal static async Task<(byte[] Main, byte[] Thumb)> CompressImageAsync(Stream input, CancellationToken ct)
    {
        // ImageSharp 解码需要可 seek 的流，先缓冲。
        await using var buffered = new MemoryStream();
        await input.CopyToAsync(buffered, ct);
        buffered.Position = 0;
        // 解码炸弹防御：先用 Identify 只读图片头拿尺寸（不分配像素缓冲），超过
        // 8000×8000 直接按无效图片拒绝，恶意 PNG（声明超大分辨率）不能解码膨胀到
        // 数 GB 内存。（ImageSharp 3.x 没有 MaxWidth/MaxHeight 解码上限选项；
        // TargetSize 是解码后缩放，防不住解码器全尺寸像素缓冲的分配，故用头信息预检。）
        var info = await Image.IdentifyAsync(buffered, ct);
        if (info is null || info.Width > 8000 || info.Height > 8000)
        {
            throw new InvalidImageContentException("image dimensions exceed the 8000x8000 limit");
        }

        // Identify 读走了流内容，重新定位到头部再解码。
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

    internal static async Task<long> GetLimitAsync(HZoneDbContext db, MediaKind kind, CancellationToken ct)
    {
        var key = kind switch
        {
            MediaKind.Image => "media.maxImageBytes",
            MediaKind.Video => "media.maxVideoBytes",
            MediaKind.File => "media.maxFileBytes",
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
                MediaKind.File => DefaultFileLimit,
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
