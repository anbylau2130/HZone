using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Core.Media;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 头像配置（composer-polish 票 05）：复用 /media 的图片压缩与存储管线，
/// 上传成功落 MediaAsset 行（ContentId 为空 = 永久媒体，不随内容级联删除）并更新 User.AvatarUrl。
/// </summary>
public static class MeAvatarEndpoints
{
    public static void MapMeAvatarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/me/avatar", [Authorize] async (
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
                    title: "avatar too large");
            }

            await using var mainStream = new MemoryStream(mainBytes);
            var storedName = await storage.SaveAsync(mainStream, file.FileName, "image/jpeg", ct);

            // 落 MediaAsset 行：GET /media/{storedName} 按 MediaAsset 查 Content-Type，缺行会 404。
            db.MediaAssets.Add(new MediaAsset
            {
                Id = Guid.NewGuid(),
                OwnerUserId = CurrentUserId(principal),
                StoredName = storedName,
                OriginalName = string.IsNullOrWhiteSpace(file.FileName) ? "avatar" : file.FileName,
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

            // 旧头像文件保留在存储中（孤儿媒体，已知简化，与随记编辑换图同口径）。
            user.AvatarUrl = storedName;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { avatarUrl = $"/media/{storedName}" });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 清空头像：仅清 User.AvatarUrl，媒体文件与 MediaAsset 行保留（孤儿媒体，已知简化）。
        app.MapDelete("/me/avatar", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            user.AvatarUrl = null;
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
