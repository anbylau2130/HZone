using System.Globalization;
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
                .Select(c => new CreatedAtCity(c.CreatedAt, c.WeatherCity))
                .ToListAsync(ct);
            return Results.Ok(new MeStatsResponse(
                new MeTotalsResponse(says, journals, friends),
                StreakOf(stampers.Select(s => s.CreatedAt).ToList(), utcNow),
                LunarToday(),
                HeatmapOf(stampers, utcNow),
                FootprintsOf(stampers)));
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

    /// <summary>今日农历三字段（中国区日期），复用 LunarCalendar，与落库口径与 streak/热力格同口径。</summary>
    private static TodayResponse LunarToday()
    {
        var (ganzhiYear, monthDay, term) =
            LunarCalendar.Compute(new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero));
        return new TodayResponse(ganzhiYear, monthDay, term);
    }

    /// <summary>
    /// 近 90 天热力格（含今天）：按中国区本地日期聚合用户内容的日计数，缺天补 0，
    /// 恒为 90 项旧→新。与 streak 同口径（China Standard Time）。
    /// </summary>
    private static List<HeatmapCellResponse> HeatmapOf(
        List<CreatedAtCity> stampers, DateTime utcNow)
    {
        var zone = ChinaZone();
        var today = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone).Date;
        var counts = new Dictionary<DateOnly, int>();
        foreach (var s in stampers)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                new DateTimeOffset(s.CreatedAt, TimeSpan.Zero), zone).Date);
            counts[day] = counts.GetValueOrDefault(day) + 1;
        }

        var heatmap = new List<HeatmapCellResponse>(90);
        for (var i = 89; i >= 0; i--)
        {
            var day = DateOnly.FromDateTime(today.AddDays(-i));
            heatmap.Add(new HeatmapCellResponse(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                counts.GetValueOrDefault(day)));
        }

        return heatmap;
    }

    /// <summary>
    /// 足迹：取有天气城市的内容按时间正序遍历，city 与前一条不同才记一条
    /// （同城连续去重，firstAt = 该城市首次出现的时刻）；无天气城市的内容跳过。
    /// 返回倒序（最近在上）。
    /// </summary>
    private static List<FootprintResponse> FootprintsOf(List<CreatedAtCity> stampers)
    {
        var footprints = new List<FootprintResponse>();
        string? previous = null;
        foreach (var s in stampers.OrderBy(s => s.CreatedAt))
        {
            if (string.IsNullOrEmpty(s.WeatherCity) || s.WeatherCity == previous) continue;
            footprints.Add(new FootprintResponse(s.WeatherCity, s.CreatedAt));
            previous = s.WeatherCity;
        }

        footprints.Reverse();
        return footprints;
    }

    // /me/contents 的 date 参数（热力格点格下钻）与 stats 聚合共用同一时区口径。
    internal static TimeZoneInfo ChinaZone()
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

/// <summary>用户内容的轻量投影：日计数聚合与足迹去重的数据源。</summary>
public sealed record CreatedAtCity(DateTime CreatedAt, string? WeatherCity);

public record MeStatsResponse(
    MeTotalsResponse Totals,
    int Streak,
    TodayResponse Today,
    IReadOnlyList<HeatmapCellResponse> Heatmap,
    IReadOnlyList<FootprintResponse> Footprints);

public record HeatmapCellResponse(string Date, int Count);

/// <summary>足迹条目：城市 + 该城市首次出现的时刻（同城连续去重后）。</summary>
public record FootprintResponse(string City, DateTime FirstAt);

public record MeTotalsResponse(int Says, int Journals, int Friends);

public record TodayResponse(string GanzhiYear, string MonthDay, string Term);
