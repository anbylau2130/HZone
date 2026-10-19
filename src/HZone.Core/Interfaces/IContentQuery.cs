namespace HZone.Core.Interfaces;

using HZone.Core.Entities;

public interface IContentQuery
{
    IQueryable<Content> VisibleTo(Guid viewerId);

    Task<Content?> FindVisibleAsync(Guid contentId, Guid viewerId, CancellationToken ct = default);
}
