using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Core.Media;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 个人资料（moments-wechat 票 01）：签名编辑与封面上传。
/// 封面复用 /me/avatar 的图片压缩与存储管线（MediaEndpoints 压缩 + IFileStorage.SaveAsync + MediaAsset 落行），
/// 更新 User.CoverUrl（存 storedName，口径同 AvatarUrl）。
/// </summary>
public static class MeProfileEndpoints
{
    public static void MapMeProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // 签名编辑：整体替换（null = 清空）。
        app.MapPut("/me/profile", [Authorize] async (
            UpdateSignatureRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            if (request.Signature is { Length: > 100 })
            {
                return Results.BadRequest(new { error = "signature must be at most 100 characters" });
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId(principal), ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            user.Signature = request.Signature;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { signature = user.Signature });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 封面上传：与 /me/avatar 同管线同校验（multipart image 字段、非图片 400、解码失败 400、超限 413）。
        app.MapPost("/me/cover", [Authorize] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart form with 'image' field required" });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files["image"] ?? form.Files["file"];
            if (file is null)
            {
                return Results.BadRequest(new { error = "multipart form with 'image' field required" });
            }

            // 非图片 Content-Type → 400（与 /media 的 ClassifyKind 同口径）。
            if (MediaEndpoints.ClassifyKind(file.ContentType) != MediaKind.Image)
            {
                return Results.BadRequest(new { error = "only images allowed" });
            }

            var limit = await MediaEndpoints.GetLimitAsync(db, MediaKind.Image, ct);
            byte[] mainBytes;
            try
            {
                (mainBytes, _) = await MediaEndpoints.CompressImageAsync(file.OpenReadStream(), ct);
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
                    title: "cover too large");
            }

            await using var mainStream = new MemoryStream(mainBytes);
            var storedName = await storage.SaveAsync(mainStream, file.FileName, "image/jpeg", ct);

            // 落 MediaAsset 行：GET /media/{storedName} 按 MediaAsset 查 Content-Type，缺行会 404。
            db.MediaAssets.Add(new MediaAsset
            {
                Id = Guid.NewGuid(),
                OwnerUserId = CurrentUserId(principal),
                StoredName = storedName,
                OriginalName = string.IsNullOrWhiteSpace(file.FileName) ? "cover" : file.FileName,
                Kind = MediaKind.Image,
                ContentType = "image/jpeg",
                SizeBytes = mainBytes.Length,
                ThumbStoredName = null,
                ContentId = null,
                CreatedAt = DateTime.UtcNow,
            });

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId(principal), ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            // 旧封面文件保留在存储中（孤儿媒体，已知简化，与头像同口径）。
            user.CoverUrl = storedName;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { coverUrl = $"/media/{storedName}" });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 清空封面：仅清 User.CoverUrl，媒体文件与 MediaAsset 行保留（孤儿媒体，已知简化）。
        app.MapDelete("/me/cover", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId(principal), ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            user.CoverUrl = null;
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

    private sealed record UpdateSignatureRequest(string? Signature);
}
