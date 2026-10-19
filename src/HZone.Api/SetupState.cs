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
