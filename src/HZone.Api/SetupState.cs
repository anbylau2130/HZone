using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api;

/// <summary>
/// 单一入口判断实例是否已完成初始化（管理员创建）。
/// </summary>
public static class SetupState
{
    public const string SetupCompletedKey = "setup.completed";

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
