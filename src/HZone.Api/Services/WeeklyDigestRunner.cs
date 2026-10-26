using System.Globalization;
using System.Text;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Api.Endpoints;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Services;

/// <summary>
/// 每周邮件周报执行器（与调度解耦，可单测）：
/// 上周一 00:00 – 本周一 00:00（北京时区，与统计口径一致）的内容汇总，
/// 发给每位已验证邮箱的用户。纯文本正文：数量总览 + 最多三篇笔记标题钩子。
/// </summary>
public sealed class WeeklyDigestRunner
{
    private readonly IEmailSender _email;

    public WeeklyDigestRunner(IEmailSender email) => _email = email;

    /// <summary>上周窗口（北京时间）：返回 (fromUtc, toUtc)，toUtc=本周一 00:00。</summary>
    public static (DateTime FromUtc, DateTime ToUtc) LastWeekWindow(DateTime utcNow)
    {
        var zone = MeStatsEndpoints.ChinaZone();
        var nowBeijing = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone);
        // 本周一 00:00（DayOfWeek.Monday=1；周日回退 6 天）
        var daysSinceMonday = ((int)nowBeijing.DayOfWeek + 6) % 7;
        var thisMonday = nowBeijing.Date.AddDays(-daysSinceMonday);
        var toBeijing = thisMonday;
        var fromBeijing = toBeijing.AddDays(-7);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromBeijing, zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(toBeijing, zone);
        return (fromUtc, toUtc);
    }

    /// <summary>上周汇总（单用户）：随记/笔记数 + 最热一天 + 笔记标题（≤3，新→旧）。</summary>
    public static async Task<(int Says, int Journals, string Body)> BuildAsync(
        HZoneDbContext db, Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var says = await db.Contents
            .OfType<Say>()
            .CountAsync(c => c.AuthorId == userId && c.CreatedAt >= fromUtc && c.CreatedAt < toUtc, ct);
        var journals = await db.Contents
            .OfType<Journal>()
            .CountAsync(c => c.AuthorId == userId && c.CreatedAt >= fromUtc && c.CreatedAt < toUtc, ct);
        var journalRows = await db.Contents
            .OfType<Journal>()
            .Where(c => c.AuthorId == userId && c.CreatedAt >= fromUtc && c.CreatedAt < toUtc)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.Title)
            .Take(3)
            .ToListAsync(ct);

        var body = new StringBuilder()
            .AppendLine(CultureInfo.CurrentCulture, $"上周你记录了 {says} 条随记、{journals} 篇笔记。");
        if (journalRows.Count > 0)
        {
            body.AppendLine("最近的笔记：");
            foreach (var t in journalRows)
            {
                body.AppendLine(CultureInfo.CurrentCulture, $"  · {t}");
            }
        }
        if (says + journals == 0)
        {
            body.AppendLine("上周没有记录——新的一周，从今天的一笔开始。");
        }
        return (says, journals, body.ToString());
    }

    /// <summary>给全部已验证用户发上周周报（逐个发送，单个失败不断批次）。</summary>
    public async Task<int> SendAllAsync(HZoneDbContext db, DateTime utcNow, CancellationToken ct)
    {
        var (fromUtc, toUtc) = LastWeekWindow(utcNow);
        var users = await db.Users
            .Where(u => u.EmailVerified && !u.IsDisabled)
            .Select(u => new { u.Id, u.Email, u.Nickname })
            .ToListAsync(ct);
        var sent = 0;
        foreach (var u in users)
        {
            try
            {
                var (_, _, body) = await BuildAsync(db, u.Id, fromUtc, toUtc, ct);
                await _email.SendAsync(u.Email, $"HZone 周报 · {toUtc.AddDays(-7):MM-dd} ~ {toUtc:MM-dd}", body, ct);
                sent++;
            }
            catch (Exception ex)
            {
                // 单个失败不断批次；调用方（HostedService）记日志
                _ = ex;
            }
        }
        return sent;
    }
}
