using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 记账（轻量手工账）：个人账本（每人一张，惰性建，不可删）+ 共同账本（拉好友共记）。
/// 金额存分；条目按自然月查询，月度汇总（收支/分类占比）服务端聚合。
/// 可见性：个人账本仅本人；共同账本成员共阅；条目改删仅记录人。
/// </summary>
public static class LedgerEndpoints
{
    private const int MaxSharedLedgersPerUser = 10;

    public static void MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        // 我的账本列表：个人账本惰性建（"我的账本"）；共同账本 = 我是创建人或成员。
        app.MapGet("/me/ledgers", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            await EnsurePersonalLedgerAsync(db, me, ct);
            var ledgers = await db.Ledgers.AsNoTracking()
                .Where(l => l.OwnerUserId == me || l.Members.Any(m => m.UserId == me))
                .OrderBy(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id,
                    l.Name,
                    l.IsShared,
                    l.OwnerUserId,
                    l.CreatedAt,
                    Members = l.Members
                        .OrderBy(m => m.JoinedAt)
                        .Select(m => new { m.UserId, m.User.Nickname }),
                })
                .ToListAsync(ct);
            return Results.Ok(new { items = ledgers });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 新建共同账本（个人账本唯一且自动，不走此端点）；建时创建人自动入成员。
        app.MapPost("/me/ledgers", [Authorize] async (
            CreateLedgerRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 50)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "name is required (max 50 chars)" },
                });
            }
            var mine = await db.Ledgers.CountAsync(
                l => l.OwnerUserId == me && l.IsShared, ct);
            if (mine >= MaxSharedLedgersPerUser)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "shared ledger limit reached");
            }
            var ledger = new Ledger
            {
                Id = Guid.NewGuid(),
                OwnerUserId = me,
                Name = name,
                IsShared = true,
                CreatedAt = DateTime.UtcNow,
            };
            ledger.Members.Add(new LedgerMember
            {
                Id = Guid.NewGuid(),
                LedgerId = ledger.Id,
                UserId = me,
                JoinedAt = DateTime.UtcNow,
            });
            db.Ledgers.Add(ledger);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/ledgers/{ledger.Id}",
                new { ledger.Id, ledger.Name, ledger.IsShared, ledger.CreatedAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 改名：共同账本成员均可。
        app.MapPut("/me/ledgers/{id:guid}", [Authorize] async (
            Guid id,
            CreateLedgerRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 50)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "name is required (max 50 chars)" },
                });
            }
            ledger.Name = name;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ledger.Id, ledger.Name });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 删除共同账本：仅创建人；个人账本不可删（400）。
        app.MapDelete("/me/ledgers/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            if (!ledger.IsShared)
            {
                return Results.BadRequest(new { error = "personal ledger cannot be deleted" });
            }
            if (ledger.OwnerUserId != me)
            {
                return Results.Forbid();
            }
            await db.LedgerEntries.Where(x => x.LedgerId == id).ExecuteDeleteAsync(ct);
            await db.LedgerMembers.Where(m => m.LedgerId == id).ExecuteDeleteAsync(ct);
            db.Ledgers.Remove(ledger);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 加成员：仅创建人；目标须为好友且未入本。
        app.MapPost("/me/ledgers/{id:guid}/members", [Authorize] async (
            Guid id,
            AddLedgerMemberRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null || !ledger.IsShared)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            if (ledger.OwnerUserId != me)
            {
                return Results.Forbid();
            }
            if (request.UserId is not Guid target)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userId"] = new[] { "userId is required" },
                });
            }
            var friends = await db.Friendships.AnyAsync(
                f => f.Status == FriendshipStatus.Accepted
                    && ((f.RequesterId == me && f.AddresseeId == target)
                        || (f.AddresseeId == me && f.RequesterId == target)), ct);
            if (!friends)
            {
                return Results.BadRequest(new { error = "target is not a friend" });
            }
            if (await db.LedgerMembers.AnyAsync(m => m.LedgerId == id && m.UserId == target, ct))
            {
                return Results.Conflict(new { error = "already a member" });
            }
            db.LedgerMembers.Add(new LedgerMember
            {
                Id = Guid.NewGuid(),
                LedgerId = id,
                UserId = target,
                JoinedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 移除成员：创建人可移除任意成员；成员可自退。
        app.MapDelete("/me/ledgers/{id:guid}/members/{userId:guid}", [Authorize] async (
            Guid id,
            Guid userId,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null || !ledger.IsShared)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            if (userId != me && ledger.OwnerUserId != me)
            {
                return Results.Forbid();
            }
            var member = await db.LedgerMembers
                .FirstOrDefaultAsync(m => m.LedgerId == id && m.UserId == userId, ct);
            if (member is null)
            {
                return Results.NotFound(new { error = "member not found" });
            }
            db.LedgerMembers.Remove(member);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 月度条目 + 服务端汇总：month=yyyy-MM（缺省当月）。
        app.MapGet("/me/ledgers/{id:guid}/entries", [Authorize] async (
            Guid id,
            string? month,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            DateOnly from, to;
            if (month is null)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                from = new DateOnly(today.Year, today.Month, 1);
            }
            else if (!DateOnly.TryParseExact(
                month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
            {
                return Results.BadRequest(new { error = "month must be yyyy-MM" });
            }
            else
            {
                from = m;
            }
            to = from.AddMonths(1);

            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(x => x.LedgerId == id && x.EntryDate >= from && x.EntryDate < to)
                .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.AmountCents,
                    Kind = x.Kind == LedgerEntryKind.Expense ? "expense" : "income",
                    x.Category,
                    x.Note,
                    x.EntryDate,
                    Recorder = new { x.RecorderUserId, x.Recorder.Nickname },
                    x.CreatedAt,
                })
                .ToListAsync(ct);

            var summary = new
            {
                ExpenseCents = entries.Where(x => x.Kind == "expense").Sum(x => x.AmountCents),
                IncomeCents = entries.Where(x => x.Kind == "income").Sum(x => x.AmountCents),
                ByCategory = entries
                    .Where(x => x.Kind == "expense")
                    .GroupBy(x => x.Category)
                    .Select(g => new { Category = g.Key, Cents = g.Sum(x => x.AmountCents) })
                    .OrderByDescending(g => g.Cents)
                    .ToList(),
            };
            return Results.Ok(new { ledgerId = id, ledgerName = ledger.Name, from, entries, summary });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 记一笔：成员均可；金额分恒正、方向由 kind 决定。
        app.MapPost("/me/ledgers/{id:guid}/entries", [Authorize] async (
            Guid id,
            CreateLedgerEntryRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var ledger = await FindAccessibleAsync(db, id, me, ct);
            if (ledger is null)
            {
                return Results.NotFound(new { error = "ledger not found" });
            }
            var kind = request.Kind == "expense" ? LedgerEntryKind.Expense
                : request.Kind == "income" ? LedgerEntryKind.Income
                : (LedgerEntryKind?)null;
            if (kind is null || request.AmountCents is <= 0 or > 1_000_000_000_00
                || string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 32
                || (request.Note?.Length ?? 0) > 200
                || !DateOnly.TryParse(request.EntryDate, out var entryDate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["entry"] = new[] { "invalid entry payload" },
                });
            }
            var entry = new LedgerEntry
            {
                Id = Guid.NewGuid(),
                LedgerId = id,
                RecorderUserId = me,
                AmountCents = request.AmountCents.Value,
                Kind = kind.Value,
                Category = request.Category.Trim(),
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                EntryDate = entryDate,
                CreatedAt = DateTime.UtcNow,
            };
            db.LedgerEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/ledger/entries/{entry.Id}",
                new { entry.Id, entry.AmountCents, Kind = request.Kind, entry.Category, entry.Note, entry.EntryDate });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 改/删条目：仅记录人本人。
        app.MapPut("/ledger/entries/{id:guid}", [Authorize] async (
            Guid id,
            CreateLedgerEntryRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var entry = await db.LedgerEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entry is null || entry.RecorderUserId != me)
            {
                return Results.NotFound(new { error = "entry not found" });
            }
            var kind = request.Kind == "expense" ? LedgerEntryKind.Expense
                : request.Kind == "income" ? LedgerEntryKind.Income
                : (LedgerEntryKind?)null;
            if (kind is null || request.AmountCents is <= 0 or > 1_000_000_000_00
                || string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 32
                || (request.Note?.Length ?? 0) > 200
                || !DateOnly.TryParse(request.EntryDate, out var entryDate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["entry"] = new[] { "invalid entry payload" },
                });
            }
            entry.AmountCents = request.AmountCents.Value;
            entry.Kind = kind.Value;
            entry.Category = request.Category.Trim();
            entry.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            entry.EntryDate = entryDate;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { entry.Id });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapDelete("/ledger/entries/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var entry = await db.LedgerEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entry is null || entry.RecorderUserId != me)
            {
                return Results.NotFound(new { error = "entry not found" });
            }
            db.LedgerEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }

    /// <summary>个人账本惰性建（每人一张"我的账本"，CreatedAt 压最小恒排最前）。</summary>
    private static async Task EnsurePersonalLedgerAsync(HZoneDbContext db, Guid userId, CancellationToken ct)
    {
        if (await db.Ledgers.AnyAsync(l => l.OwnerUserId == userId && !l.IsShared, ct))
        {
            return;
        }
        db.Ledgers.Add(new Ledger
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Name = "我的账本",
            IsShared = false,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>可见账本：个人账本本人；共同账本创建人或成员。不可见/不存在统一 null（404）。</summary>
    private static async Task<Ledger?> FindAccessibleAsync(
        HZoneDbContext db, Guid ledgerId, Guid me, CancellationToken ct) =>
        await db.Ledgers.FirstOrDefaultAsync(
            l => l.Id == ledgerId
                && (!l.IsShared
                    ? l.OwnerUserId == me
                    : l.OwnerUserId == me || l.Members.Any(m => m.UserId == me)),
            ct);
}

public sealed record CreateLedgerRequest(string? Name);

public sealed record AddLedgerMemberRequest(Guid? UserId);

public sealed record CreateLedgerEntryRequest(
    long? AmountCents,
    string? Kind,
    string? Category,
    string? Note,
    string? EntryDate);
