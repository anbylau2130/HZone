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
                .Select(c => new CreatedAtCity(
                    c.CreatedAt,
                    EF.Property<string>(c, "Discriminator"),
                    c.WeatherCity,
                    c.WeatherCondition))
                .ToListAsync(ct);
            return Results.Ok(new MeStatsResponse(
                new MeTotalsResponse(says, journals, friends),
                StreakOf(stampers.Select(s => s.CreatedAt).ToList(), utcNow),
                LunarToday(),
                HeatmapOf(stampers, utcNow),
                FootprintsOf(stampers),
                MonthlyOf(stampers, utcNow)));
        }).AddEndpointFilter(ContentEndpoints.RequireInitialized);

        // 那年今天（homepage-v2 ticket 06）：往年（不含今年）同月同日的内容，倒序限 5 条；无则空数组。
        app.MapGet("/me/on-this-day", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var zone = ChinaZone();
            var today = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero), zone).Date;
            var startOfCurrentYearUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(today.Year, 1, 1), zone);

            var says = await db.Contents.OfType<Say>()
                .Where(c => c.AuthorId == me && c.CreatedAt < startOfCurrentYearUtc)
                .Select(c => new OnThisDaySource(c.Id, "say", null, c.Text, c.CreatedAt))
                .ToListAsync(ct);
            var journals = await db.Contents.OfType<Journal>()
                .Where(c => c.AuthorId == me && c.CreatedAt < startOfCurrentYearUtc)
                .Select(c => new OnThisDaySource(c.Id, "journal", c.Title, null, c.CreatedAt))
                .ToListAsync(ct);

            var items = says.Concat(journals)
                .Select(s => (Source: s, Day: TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(s.CreatedAt, TimeSpan.Zero), zone).Date))
                .Where(x => x.Day.Month == today.Month && x.Day.Day == today.Day)
                .OrderByDescending(x => x.Source.CreatedAt)
                .Take(5)
                .Select(x => new OnThisDayItemResponse(
                    x.Source.Id,
                    x.Source.Kind,
                    x.Source.Title,
                    x.Source.Kind == "say" ? SummaryOf(x.Source.Text!) : null,
                    x.Day.Year))
                .ToList();
            return Results.Ok(items);
        }).AddEndpointFilter(ContentEndpoints.RequireInitialized);

        // 互动摘要（homepage-v2 ticket 06）：近 7 天我内容收到的评论数 + 我被 @ 的内容数（直查统计）。
        app.MapGet("/me/interactions/summary", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var comments = await db.Comments
                .CountAsync(c => c.Content.AuthorId == me && c.CreatedAt >= cutoff, ct);
            var mentions = await db.InboxMessages
                .CountAsync(m => m.UserId == me && m.Type == InboxMessageType.Mentioned && m.CreatedAt >= cutoff, ct);
            return Results.Ok(new InteractionSummaryResponse(comments, mentions));
        }).AddEndpointFilter(ContentEndpoints.RequireInitialized);
    }

    /// <summary>随记文本摘要：截断到 60 字符（那年今天行内一行展示）。</summary>
    private static string SummaryOf(string text)
        => text.Length <= 60 ? text : text[..60] + "…";

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

    /// <summary>
    /// 月度回顾（ticket 06）：本月（中国区）篇数、有天气的不同城市数、出现最多的天气现象
    /// （无天气现象为 null，并列取字典序最小保证稳定）。与 streak/热力格同口径。
    /// </summary>
    private static MeMonthlyResponse MonthlyOf(List<CreatedAtCity> stampers, DateTime utcNow)
    {
        var zone = ChinaZone();
        var nowLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone);
        var says = 0;
        var journals = 0;
        var cities = new HashSet<string>();
        var conditions = new Dictionary<string, int>();
        foreach (var s in stampers)
        {
            var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(s.CreatedAt, TimeSpan.Zero), zone);
            if (local.Year != nowLocal.Year || local.Month != nowLocal.Month) continue;
            if (s.Kind == nameof(Say)) says++; else journals++;
            if (!string.IsNullOrEmpty(s.WeatherCity)) cities.Add(s.WeatherCity);
            if (!string.IsNullOrEmpty(s.WeatherCondition))
            {
                conditions[s.WeatherCondition] = conditions.GetValueOrDefault(s.WeatherCondition) + 1;
            }
        }

        string? topCondition = null;
        if (conditions.Count > 0)
        {
            topCondition = conditions
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .First().Key;
        }

        return new MeMonthlyResponse(says, journals, cities.Count, topCondition);
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

/// <summary>
/// 用户内容的轻量投影：streak/热力格/足迹去重/月度回顾共用的数据源（Kind = EF 鉴别器）。
/// </summary>
public sealed record CreatedAtCity(DateTime CreatedAt, string Kind, string? WeatherCity, string? WeatherCondition);

/// <summary>那年今天的查询投影：say 取文本、journal 取标题。</summary>
public sealed record OnThisDaySource(Guid Id, string Kind, string? Title, string? Text, DateTime CreatedAt);

/// <summary>那年今天条目：精简 DTO（author 忽略），say 给摘要、journal 给标题。</summary>
public record OnThisDayItemResponse(Guid Id, string Kind, string? Title, string? Summary, int Year);

/// <summary>互动摘要（近 7 天）：我内容收到的评论数 + 我被 @ 的内容数。</summary>
public record InteractionSummaryResponse(int Comments, int Mentions);

/// <summary>月度回顾：本月篇数（分 kind）、有天气的不同城市数、出现最多的天气现象（可为 null）。</summary>
public record MeMonthlyResponse(int Says, int Journals, int Cities, string? TopCondition);

public record MeStatsResponse(
    MeTotalsResponse Totals,
    int Streak,
    TodayResponse Today,
    IReadOnlyList<HeatmapCellResponse> Heatmap,
    IReadOnlyList<FootprintResponse> Footprints,
    MeMonthlyResponse Monthly);

public record HeatmapCellResponse(string Date, int Count);

/// <summary>足迹条目：城市 + 该城市首次出现的时刻（同城连续去重后）。</summary>
public record FootprintResponse(string City, DateTime FirstAt);

public record MeTotalsResponse(int Says, int Journals, int Friends);

public record TodayResponse(string GanzhiYear, string MonthDay, string Term);
