using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Lunar;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class MeStatsEndpoints
{
    public static void MapMeStatsEndpoints(this IEndpointRouteBuilder app)
    {
        // 首页统计（homepage-v2 ticket 04）：四卡数字 + 连续记录天数 + 今日农历。
        app.MapGet("/me/stats", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);

            var says = await db.Contents.OfType<Say>().CountAsync(c => c.AuthorId == me, ct);
            var journals = await db.Contents.OfType<Journal>().CountAsync(c => c.AuthorId == me, ct);
            var friends = await db.Friendships.CountAsync(
                f => f.Status == FriendshipStatus.Accepted && (f.RequesterId == me || f.AddresseeId == me), ct);

            var utcNow = DateTime.UtcNow;
            var stampers = await db.Contents
                .Where(c => c.AuthorId == me)
                .Select(c => c.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(new MeStatsResponse(
                new MeTotalsResponse(says, journals, friends),
                StreakOf(stampers, utcNow),
                LunarToday()));
        }).AddEndpointFilter(ContentEndpoints.RequireInitialized);
    }

    /// <summary>
    /// 连续记录天数：按中国标准时间（UTC+8，与农历同口径）把每条内容的 CreatedAt
    /// 归到本地日期；从今天往回数连续有内容的天数。今天还没发不断签——昨天有内容时
    /// 从昨天起数；今天与昨天都没有则归 0。时间戳量级与内容数同阶，内存聚合即可。
    /// </summary>
    private static int StreakOf(List<DateTime> createdAts, DateTime utcNow)
    {
        var zone = ChinaZone();
        var today = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone).Date;
        var days = createdAts
            .Select(t => TimeZoneInfo.ConvertTime(new DateTimeOffset(t, TimeSpan.Zero), zone).Date)
            .ToHashSet();

        var cursor = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    /// <summary>今日农历三字段（中国区日期），复用 LunarCalendar，与落库口径一致。</summary>
    private static TodayResponse LunarToday()
    {
        var (ganzhiYear, monthDay, term) =
            LunarCalendar.Compute(new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero));
        return new TodayResponse(ganzhiYear, monthDay, term);
    }

    private static TimeZoneInfo ChinaZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (Exception)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
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

public record MeStatsResponse(MeTotalsResponse Totals, int Streak, TodayResponse Today);

public record MeTotalsResponse(int Says, int Journals, int Friends);

public record TodayResponse(string GanzhiYear, string MonthDay, string Term);
