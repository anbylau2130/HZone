using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class AdminEndpoints
{
    public const string InviteEnabledKey = "invite.enabled";
    public const string MaxImageBytesKey = "media.maxImageBytes";
    public const string MaxVideoBytesKey = "media.maxVideoBytes";
    public const string MaxVideoSecondsKey = "media.maxVideoSeconds";
    public const string MaxAudioBytesKey = "media.maxAudioBytes";
    public const string SmtpHostKey = "smtp.host";
    public const string SmtpPortKey = "smtp.port";
    public const string SmtpUsernameKey = "smtp.username";
    public const string SmtpPasswordKey = "smtp.password";
    public const string SmtpFromAddressKey = "smtp.fromAddress";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/config", [Authorize] async (
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var configs = await db.SystemConfigs.AsNoTracking()
                .ToDictionaryAsync(c => c.Key, c => c.Value, ct);
            return Results.Ok(BuildConfigResponse(configs));
        }).AddEndpointFilter(SetupState.RequireAdmin);

        app.MapPut("/admin/config", [Authorize] async (
            AdminConfigUpdateRequest request,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var errors = ValidateUpdate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            if (request.InviteEnabled is { } inviteEnabled)
            {
                await SetConfigAsync(db, InviteEnabledKey, inviteEnabled ? "true" : "false", ct);
            }

            if (request.MediaLimits is { } limits)
            {
                if (limits.ImageBytes is { } imageBytes)
                {
                    await SetConfigAsync(db, MaxImageBytesKey, imageBytes.ToString(CultureInfo.InvariantCulture), ct);
                }

                if (limits.VideoBytes is { } videoBytes)
                {
                    await SetConfigAsync(db, MaxVideoBytesKey, videoBytes.ToString(CultureInfo.InvariantCulture), ct);
                }

                if (limits.VideoMaxSeconds is { } videoSeconds)
                {
                    await SetConfigAsync(db, MaxVideoSecondsKey, videoSeconds.ToString(CultureInfo.InvariantCulture), ct);
                }

                if (limits.AudioBytes is { } audioBytes)
                {
                    await SetConfigAsync(db, MaxAudioBytesKey, audioBytes.ToString(CultureInfo.InvariantCulture), ct);
                }
            }

            if (request.Smtp is { } smtp)
            {
                if (smtp.Host is { } host)
                {
                    await SetConfigAsync(db, SmtpHostKey, host, ct);
                }

                if (smtp.Port is { } port)
                {
                    await SetConfigAsync(db, SmtpPortKey, port.ToString(CultureInfo.InvariantCulture), ct);
                }

                if (smtp.Username is { } username)
                {
                    await SetConfigAsync(db, SmtpUsernameKey, username, ct);
                }

                if (smtp.FromAddress is { } fromAddress)
                {
                    await SetConfigAsync(db, SmtpFromAddressKey, fromAddress, ct);
                }

                if (!string.IsNullOrEmpty(smtp.Password))
                {
                    await SetConfigAsync(db, SmtpPasswordKey, smtp.Password, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            var configs = await db.SystemConfigs.AsNoTracking()
                .ToDictionaryAsync(c => c.Key, c => c.Value, ct);
            return Results.Ok(BuildConfigResponse(configs));
        }).AddEndpointFilter(SetupState.RequireAdmin);

        app.MapGet("/admin/users", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct,
            int offset = 0,
            int limit = 20) =>
        {
            var offsetClamped = Math.Max(0, offset);
            var limitClamped = Math.Clamp(limit, 1, 100);
            var total = await db.Users.CountAsync(ct);
            var items = await db.Users
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Skip(offsetClamped).Take(limitClamped)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Nickname,
                    u.IsAdmin,
                    u.EmailVerified,
                    u.IsDisabled,
                    u.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { items, total });
        }).AddEndpointFilter(SetupState.RequireAdmin);

        app.MapPut("/admin/users/{id}/disable", [Authorize] async (
            Guid id,
            DisableUserRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var currentUserId = CurrentUserId(principal);
            if (id == currentUserId)
            {
                return Results.BadRequest(new { error = "cannot disable yourself" });
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            user.IsDisabled = request.IsDisabled;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true, isDisabled = user.IsDisabled });
        }).AddEndpointFilter(SetupState.RequireAdmin);
    }

    private static Dictionary<string, string[]> ValidateUpdate(AdminConfigUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.MediaLimits is { } limits)
        {
            if (limits.ImageBytes is <= 0)
            {
                errors["mediaLimits.imageBytes"] = new[] { "must be a positive number" };
            }

            if (limits.VideoBytes is <= 0)
            {
                errors["mediaLimits.videoBytes"] = new[] { "must be a positive number" };
            }

            if (limits.VideoMaxSeconds is <= 0)
            {
                errors["mediaLimits.videoMaxSeconds"] = new[] { "must be a positive number" };
            }

            if (limits.AudioBytes is <= 0)
            {
                errors["mediaLimits.audioBytes"] = new[] { "must be a positive number" };
            }
        }

        if (request.Smtp is { } smtp && smtp.Port is { } port && (port < 1 || port > 65535))
        {
            errors["smtp.port"] = new[] { "port must be between 1 and 65535" };
        }

        return errors;
    }

    private static object BuildConfigResponse(IReadOnlyDictionary<string, string> configs)
    {
        var inviteEnabled = configs.GetValueOrDefault(InviteEnabledKey) != "false";
        var mediaLimits = new MediaLimitsResponse(
            ImageBytes: ParsePositiveLong(configs, MaxImageBytesKey) ?? MediaEndpoints.DefaultImageLimit,
            VideoBytes: ParsePositiveLong(configs, MaxVideoBytesKey) ?? MediaEndpoints.DefaultVideoLimit,
            VideoMaxSeconds: (int?)ParsePositiveLong(configs, MaxVideoSecondsKey) ?? MediaEndpoints.DefaultVideoMaxSeconds,
            AudioBytes: ParsePositiveLong(configs, MaxAudioBytesKey) ?? MediaEndpoints.DefaultAudioLimit);
        var smtp = new SmtpStatusResponse(
            Host: configs.GetValueOrDefault(SmtpHostKey),
            Port: int.TryParse(configs.GetValueOrDefault(SmtpPortKey), out var port) ? port : null,
            Username: configs.GetValueOrDefault(SmtpUsernameKey),
            FromAddress: configs.GetValueOrDefault(SmtpFromAddressKey),
            HasPassword: configs.ContainsKey(SmtpPasswordKey));
        return new AdminConfigResponse(inviteEnabled, mediaLimits, smtp);
    }

    private static long? ParsePositiveLong(IReadOnlyDictionary<string, string> configs, string key)
    {
        return long.TryParse(configs.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    private static async Task SetConfigAsync(HZoneDbContext db, string key, string value, CancellationToken ct)
    {
        var existing = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (existing is null)
        {
            db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public sealed record AdminConfigResponse(bool InviteEnabled, MediaLimitsResponse MediaLimits, SmtpStatusResponse Smtp);
public sealed record MediaLimitsResponse(long ImageBytes, long VideoBytes, int VideoMaxSeconds, long AudioBytes);
public sealed record SmtpStatusResponse(string? Host, int? Port, string? Username, string? FromAddress, bool HasPassword);

public sealed record AdminConfigUpdateRequest(
    bool? InviteEnabled,
    AdminMediaLimitsUpdate? MediaLimits,
    AdminSmtpUpdate? Smtp);
public sealed record AdminMediaLimitsUpdate(long? ImageBytes, long? VideoBytes, int? VideoMaxSeconds, long? AudioBytes);
public sealed record AdminSmtpUpdate(string? Host, int? Port, string? Username, string? Password, string? FromAddress);
public sealed record DisableUserRequest(bool IsDisabled);
