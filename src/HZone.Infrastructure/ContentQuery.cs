using HZone.Core.Entities;
using HZone.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HZone.Infrastructure;

public class ContentQuery : IContentQuery
{
    private readonly HZoneDbContext _db;

    public ContentQuery(HZoneDbContext db)
    {
        _db = db;
    }

    public IQueryable<Content> VisibleTo(Guid viewerId)
    {
        return _db.Contents.Where(c =>
            c.AuthorId == viewerId ||
            c.Recipients.Any(r => r.RecipientUserId == viewerId));
    }

    public async Task<Content?> FindVisibleAsync(Guid contentId, Guid viewerId, CancellationToken ct = default)
    {
        return await VisibleTo(viewerId)
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);
    }

    public async Task<(List<Content> Items, int Total)> QueryAsync(ContentQuerySpec spec, CancellationToken ct = default)
    {
        var query = VisibleTo(spec.ViewerId);

        if (spec.AuthorId is { } authorId)
        {
            query = query.Where(c => c.AuthorId == authorId);
        }

        if (spec.ExcludeAuthorId is { } excluded)
        {
            query = query.Where(c => c.AuthorId != excluded);
        }

        if (spec.RecipientId is { } recipientId)
        {
            query = query.Where(c => c.Recipients.Any(r => r.RecipientUserId == recipientId));
        }

        if (spec.SearchText is not null)
        {
            // LIKE 通配符转义（\ % _），用户输入按字面匹配。
            // 必须在 OfType<Say/Journal> 之前应用：OfType 后元素类型已收窄，
            // 表达式里的 c is Journal 会触发 EF Say/Journal 强制转换 500
            var pattern = "%" + EscapeLike(spec.SearchText) + "%";
            query = query.Where(c =>
                (c is Say && EF.Functions.Like(((Say)c).Text, pattern, "\\"))
                || (c is Journal && (EF.Functions.Like(((Journal)c).Title, pattern, "\\")
                    || EF.Functions.Like(((Journal)c).MarkdownBody, pattern, "\\"))));
        }

        if (spec.Tag is { Length: > 0 } tag)
        {
            query = query.Where(c => c.Tags.Any(t => t.Tag == tag));
        }

        switch (spec.Kind)
        {
            case "say":
                query = query.OfType<Say>();
                break;
            case "journal":
                query = query.OfType<Journal>();
                break;
        }

        if (spec.CreatedFrom is { } from)
        {
            query = query.Where(c => c.CreatedAt >= from);
        }

        if (spec.CreatedTo is { } to)
        {
            query = query.Where(c => c.CreatedAt < to);
        }

        // 键集游标：与下方 CreatedAt+Id 双键倒序配套，(CreatedAt, Id) 严格小于游标。
        if (spec.CursorCreatedAt is { } cursorAt && spec.CursorId is { } cursorId)
        {
            query = query.Where(c => c.CreatedAt < cursorAt
                || (c.CreatedAt == cursorAt && c.Id.CompareTo(cursorId) < 0));
        }

        var total = await query.CountAsync(ct);
        // 加 Id 作唯一次序键：同 CreatedAt（如 /import 批量导入全是同一 UtcNow）时
        // Skip/Take 不再因排序不稳定而重复/漏项。
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(spec.Offset)
            .Take(spec.Limit)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>LIKE 通配符按字面匹配：\ % _ 前加反斜杠转义。</summary>
    private static string EscapeLike(string input) => input
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
