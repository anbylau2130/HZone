using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 全局搜索：一次跨「我的+收到的」随记正文、笔记标题/正文、以及这些内容下的评论。
/// 组合筛选：类型（全部/随记/笔记/评论）、标签、时间范围（yyyy-MM-dd，中国区自然日）。
/// 命中带上下文片段（关键词前后 ±32 字符，客户端负责高亮）。
/// 单人数据量级：每源截取 500 条后内存合并分页，足够且防大库拖库。
/// </summary>
public static class SearchAllEndpoints
{
    private const int PerSourceCap = 500;
    private const int SnippetRadius = 32;

    public static void MapSearchAllEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/search-all", [Authorize] async (
            string q,
            string? kind,
            string? tag,
            string? from,
            string? to,
            int? offset,
            int? limit,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var keyword = q.Trim();
            if (keyword.Length is < 1 or > 100)
            {
                return Results.BadRequest(new { error = "q is required (max 100 chars)" });
            }
            if (kind is not (null or "say" or "journal" or "comment"))
            {
                return Results.BadRequest(new { error = "invalid kind" });
            }

            // 中国区自然日 → UTC 区间（与热力格下钻同口径）
            DateTime? fromUtc = null, toUtc = null;
            if (from is not null
                && DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fromDay))
            {
                fromUtc = TimeZoneInfo.ConvertTimeToUtc(
                    fromDay.ToDateTime(TimeOnly.MinValue), ChinaZone());
            }
            if (to is not null
                && DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var toDay))
            {
                toUtc = TimeZoneInfo.ConvertTimeToUtc(
                    toDay.ToDateTime(TimeOnly.MinValue), ChinaZone()).AddDays(1);
            }

            var pattern = "%" + EscapeLike(keyword) + "%";
            var results = new List<SearchHit>();

            // ---------- 内容（随记/笔记分开查：TPH 派生列不能在基查询投影/过滤） ----------
            if (kind is null or "say" or "journal")
            {
                List<(Guid Id, string Type, string? Title, string Source, Guid AuthorId, DateTime CreatedAt)> contentRows = new();

                if (kind is null or "say")
                {
                    var saysQ = db.Says.Where(c =>
                        c.AuthorId == me || c.Recipients.Any(r => r.RecipientUserId == me));
                    if (fromUtc is { } fu) saysQ = saysQ.Where(c => c.CreatedAt >= fu);
                    if (toUtc is { } tu) saysQ = saysQ.Where(c => c.CreatedAt < tu);
                    if (!string.IsNullOrWhiteSpace(tag)) saysQ = saysQ.Where(c => c.Tags.Any(t => t.Tag == tag.Trim()));
                    var sayRows = await saysQ
                        .Where(c => EF.Functions.Like(c.Text, pattern, "\\"))
                        .OrderByDescending(c => c.CreatedAt)
                        .Take(PerSourceCap)
                        .ToListAsync(ct);
                    contentRows.AddRange(sayRows.Select(c =>
                        (c.Id, "say", (string?)null, c.Text, c.AuthorId, c.CreatedAt)));
                }

                if (kind is null or "journal")
                {
                    var jq = db.Journals.Where(c =>
                        c.AuthorId == me || c.Recipients.Any(r => r.RecipientUserId == me));
                    if (fromUtc is { } fu) jq = jq.Where(c => c.CreatedAt >= fu);
                    if (toUtc is { } tu) jq = jq.Where(c => c.CreatedAt < tu);
                    if (!string.IsNullOrWhiteSpace(tag)) jq = jq.Where(c => c.Tags.Any(t => t.Tag == tag.Trim()));
                    var jrows = await jq
                        .Where(c => EF.Functions.Like(c.Title, pattern, "\\")
                            || EF.Functions.Like(c.MarkdownBody, pattern, "\\"))
                        .OrderByDescending(c => c.CreatedAt)
                        .Take(PerSourceCap)
                        .ToListAsync(ct);
                    contentRows.AddRange(jrows.Select(c =>
                        (c.Id, "journal", (string?)c.Title, c.Title + " " + c.MarkdownBody, c.AuthorId, c.CreatedAt)));
                }

                // Content 无 Author 导航：批查昵称（一次查询）
                var authorIds = contentRows.Select(r => r.AuthorId).Distinct().ToList();
                var nicknames = await db.Users.AsNoTracking()
                    .Where(u => authorIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);

                foreach (var r in contentRows)
                {
                    results.Add(new SearchHit(
                        r.Id, r.Type, r.Id, r.Title,
                        SnippetOf(r.Source, keyword),
                        nicknames.GetValueOrDefault(r.AuthorId, ""), r.CreatedAt, r.AuthorId == me, r.Type));
                }
            }

            // ---------- 评论（可见内容下的评论；tag 过滤作用于其内容） ----------
            if (kind is null or "comment")
            {
                var comments = db.Comments.Where(x =>
                    x.Content.AuthorId == me
                    || x.Content.Recipients.Any(r => r.RecipientUserId == me));
                if (fromUtc is { } fu2) comments = comments.Where(x => x.CreatedAt >= fu2);
                if (toUtc is { } tu2) comments = comments.Where(x => x.CreatedAt < tu2);
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    comments = comments.Where(x => x.Content.Tags.Any(t => t.Tag == tag.Trim()));
                }

                var rows = await comments
                    .Where(x => EF.Functions.Like(x.Text, pattern, "\\"))
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(PerSourceCap)
                    .Select(x => new
                    {
                        x.Id,
                        x.ContentId,
                        ContentIsJournal = EF.Property<string>(x.Content, "Discriminator") == "Journal",
                        x.Text,
                        x.AuthorId,
                        Author = x.Author.Nickname,
                        x.CreatedAt,
                        ContentIsMine = x.Content.AuthorId == me,
                    })
                    .ToListAsync(ct);

                foreach (var r in rows)
                {
                    results.Add(new SearchHit(
                        r.Id, "comment", r.ContentId, null,
                        SnippetOf(r.Text, keyword), r.Author, r.CreatedAt, r.ContentIsMine,
                        r.ContentIsJournal ? "journal" : "say"));
                }
            }

            results.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            var skip = Math.Max(0, offset ?? 0);
            var take = limit is null or <= 0 ? 20 : Math.Min(limit.Value, 50);
            return Results.Ok(new
            {
                items = results.Skip(skip).Take(take),
                total = results.Count,
                offset = skip,
                limit = take,
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

    private static TimeZoneInfo ChinaZone() =>
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    /// <summary>LIKE 通配符按字面匹配（与 ContentQuery 同规则）。</summary>
    private static string EscapeLike(string input) => input
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    /// <summary>关键词上下文片段：首个（忽略大小写）命中处前后 ±32 字符。</summary>
    private static string SnippetOf(string text, string keyword)
    {
        if (text.Length <= SnippetRadius * 2 + keyword.Length)
        {
            return text;
        }
        var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text[..Math.Min(text.Length, SnippetRadius * 2)];
        var start = Math.Max(0, idx - SnippetRadius);
        var len = Math.Min(text.Length - start, keyword.Length + SnippetRadius * 2);
        return (start > 0 ? "…" : "") + text.Substring(start, len) + (start + len < text.Length ? "…" : "");
    }

    private sealed record SearchHit(
        Guid Id,
        string Type,
        Guid ContentId,
        string? Title,
        string Snippet,
        string Author,
        DateTime CreatedAt,
        bool IsMine,
        /// 跳转目标内容类型（评论命中时 = 其内容类型；内容命中 = 自身）。
        string TargetKind);
}
