using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 年度报告（"你的这一年"数据故事）：按中国区自然年聚合——篇数/记录天数/
/// 最长连续/城市/天气分布/深夜篇数/高频标签/记录之最/收到的互动/年度高光。
/// 数据量级为单人全年（几百条封顶），一次内存聚合即可。
/// </summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/report", [Authorize] async (
            int? year,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var zone = MeStatsEndpoints.ChinaZone();

            // 年份缺省 = 最新有内容的年份；候选年份一并返回供前端切换
            // TPH 派生列（Say.Text/Journal.Title 等）不能在基查询投影——分型查询后内存合并
            var rows = new List<ReportRow>();
            rows.AddRange((await db.Says.AsNoTracking()
                .Where(c => c.AuthorId == me)
                .Select(c => new { c.Id, c.Text, c.WeatherCity, c.WeatherCondition, c.CreatedAt })
                .ToListAsync(ct))
                .Select(x => new ReportRow(x.Id, "Say", x.Text, null, null, x.WeatherCity, x.WeatherCondition, x.CreatedAt)));
            rows.AddRange((await db.Journals.AsNoTracking()
                .Where(c => c.AuthorId == me)
                .Select(c => new { c.Id, c.Title, c.MarkdownBody, c.WeatherCity, c.WeatherCondition, c.CreatedAt })
                .ToListAsync(ct))
                .Select(x => new ReportRow(x.Id, "Journal", null, x.Title, x.MarkdownBody, x.WeatherCity, x.WeatherCondition, x.CreatedAt)));
            var allContents = rows;

            int YearOf(DateTime utc) =>
                TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), zone).Year;
            var years = allContents.Select(c => YearOf(c.CreatedAt)).Distinct().OrderByDescending(y => y).ToList();
            if (years.Count == 0)
            {
                return Results.Ok(new
                {
                    years = Array.Empty<int>(),
                    year = year ?? TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Year,
                    empty = true,
                });
            }
            var target = year is not null && years.Contains(year.Value) ? year.Value : years[0];
            var mine = allContents.Where(c => YearOf(c.CreatedAt) == target).ToList();
            var ids = mine.Select(c => c.Id).ToList();

            // ---------- 天数与连续（中国区自然日） ----------
            var days = mine
                .Select(c => DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(new DateTimeOffset(c.CreatedAt, TimeSpan.Zero), zone).Date))
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            var longestStreak = 0;
            for (var i = 0; i < days.Count; )
            {
                var j = i;
                while (j + 1 < days.Count && days[j + 1].DayNumber == days[j].DayNumber + 1) j++;
                longestStreak = Math.Max(longestStreak, j - i + 1);
                i = j + 1;
            }

            // ---------- 城市 / 天气 / 深夜 ----------
            var cities = mine
                .Where(c => !string.IsNullOrEmpty(c.WeatherCity))
                .GroupBy(c => c.WeatherCity!)
                .Select(g => new { City = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).ThenBy(g => g.City)
                .ToList();
            var weather = mine
                .Where(c => !string.IsNullOrEmpty(c.WeatherCondition))
                .GroupBy(c => c.WeatherCondition!)
                .Select(g => new { Condition = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).ThenBy(g => g.Condition)
                .ToList();
            int CnHour(DateTime utc) =>
                TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), zone).Hour;
            var nightCount = mine.Count(c => { var h = CnHour(c.CreatedAt); return h >= 22 || h < 5; });
            var latest = mine.OrderByDescending(c => c.CreatedAt).FirstOrDefault();

            // ---------- 标签 ----------
            var tags = await db.ContentTags.AsNoTracking()
                .Where(t => ids.Contains(t.ContentId))
                .GroupBy(t => t.Tag)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).ThenBy(g => g.Tag)
                .Take(5)
                .ToListAsync(ct);

            // ---------- 媒体（绑定到本年内容的图片/语音） ----------
            var media = await db.MediaAssets.AsNoTracking()
                .Where(m => m.OwnerUserId == me && m.ContentId != null && ids.Contains(m.ContentId.Value))
                .Select(m => new { Kind = m.Kind })
                .ToListAsync(ct);
            var imageCount = media.Count(m => m.Kind == MediaKind.Image);
            var voiceCount = media.Count(m => m.Kind == MediaKind.Audio);

            // ---------- 收到的互动（评论/赞，非本人） ----------
            var commentsReceived = await db.Comments.AsNoTracking()
                .CountAsync(x => ids.Contains(x.ContentId) && x.AuthorId != me, ct);
            var likes = await db.ContentLikes.AsNoTracking()
                .Where(x => ids.Contains(x.ContentId) && x.UserId != me)
                .Select(x => new { x.ContentId })
                .ToListAsync(ct);
            var likesReceived = likes.Count;

            // ---------- 之最 ----------
            var longestJournal = mine
                .Where(c => c.Kind == "Journal")
                .Select(c => new { c.Id, c.Title, Chars = (c.Body ?? "").Length, c.CreatedAt })
                .OrderByDescending(c => c.Chars)
                .FirstOrDefault();
            var mostLiked = likes.Count == 0 ? null : mine
                .Where(c => c.Kind == "Say")
                .Select(c => new
                {
                    c.Id,
                    Snippet = (c.Text ?? "").Length > 60 ? c.Text![..60] : c.Text,
                    Likes = likes.Count(l => l.ContentId == c.Id),
                    c.CreatedAt,
                })
                .OrderByDescending(c => c.Likes)
                .FirstOrDefault(c => c.Likes > 0);

            var sayCount = mine.Count(c => c.Kind == "Say");
            var journalCount = mine.Count - sayCount;
            return Results.Ok(new
            {
                years,
                year = target,
                sayCount,
                journalCount,
                recordedDays = days.Count,
                longestStreak,
                cities,
                weather,
                nightCount,
                latestAt = latest?.CreatedAt,
                tags,
                imageCount,
                voiceCount,
                commentsReceived,
                likesReceived,
                longestJournal,
                highlight = mostLiked,
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }

    /// <summary>报告聚合行（随记/笔记统一形状；分型查询后合并）。</summary>
    private sealed record ReportRow(
        Guid Id,
        string Kind,
        string? Text,
        string? Title,
        string? Body,
        string? WeatherCity,
        string? WeatherCondition,
        DateTime CreatedAt);
}
