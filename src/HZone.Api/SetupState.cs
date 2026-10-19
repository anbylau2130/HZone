using System.Security.Claims;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api;

/// <summary>
/// 单一入口判断实例是否已完成初始化（管理员创建）。
/// </summary>
public static class SetupState
{
    public const string SetupCompletedKey = "setup.completed";

    /// <summary>
    /// 端点过滤器：实例未初始化时返回 409。业务端点统一挂载。
    /// </summary>
    public static readonly Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>>
        RequireInitialized = async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
            if (!await IsInitializedAsync(db, context.HttpContext.RequestAborted))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "instance not initialized");
            }

            return await next(context);
        };

    /// <summary>
    /// 端点过滤器：先初始化检查（未初始化 409），再管理员检查（非管理员 403）。管理员端点统一挂载。
    /// </summary>
    public static readonly Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>>
        RequireAdmin = async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
            if (!await IsInitializedAsync(db, context.HttpContext.RequestAborted))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "instance not initialized");
            }

            var sub = context.HttpContext.User.FindFirstValue("sub");
            var isAdmin = Guid.TryParse(sub, out var userId) && await db.Users.AnyAsync(
                u => u.Id == userId && u.IsAdmin && !u.IsDisabled,
                context.HttpContext.RequestAborted);
            if (!isAdmin)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "admin only");
            }

            return await next(context);
        };

    public static async Task<bool> IsInitializedAsync(HZoneDbContext db, CancellationToken ct = default)
    {
        var adminExists = await db.Users.AnyAsync(u => u.IsAdmin && !u.IsDisabled, ct);
        if (adminExists)
        {
            return true;
        }

        return await db.SystemConfigs.AnyAsync(c => c.Key == SetupCompletedKey, ct);
    }
}
