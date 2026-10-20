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

        if (spec.SearchText is not null)
        {
            // LIKE 通配符转义（\ % _），用户输入按字面匹配。
            var pattern = "%" + EscapeLike(spec.SearchText) + "%";
            query = query.Where(c =>
                (c is Say && EF.Functions.Like(((Say)c).Text, pattern, "\\"))
                || (c is Journal && (EF.Functions.Like(((Journal)c).Title, pattern, "\\")
                    || EF.Functions.Like(((Journal)c).MarkdownBody, pattern, "\\"))));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
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
