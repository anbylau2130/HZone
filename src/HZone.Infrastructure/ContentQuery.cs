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
}
