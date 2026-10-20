using System.Globalization;
using System.Security.Claims;
using HZone.Core.Entities;
using HZone.Core.Lunar;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class ContentEndpoints
{
    internal static readonly Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>>
        RequireInitialized = async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
            if (!await SetupState.IsInitializedAsync(db, context.HttpContext.RequestAborted))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "instance not initialized");
            }

            return await next(context);
        };

    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/contents/says", [Authorize] async (
            CreateSayRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var recipientsResult = await ValidateRecipientsAsync(db, authorId, request.RecipientIds, ct);
            if (recipientsResult.Error is not null)
            {
                return Results.BadRequest(new { error = recipientsResult.Error });
            }

            var now = DateTime.UtcNow;
            var say = new Say
            {
                Id = Guid.NewGuid(),
                AuthorId = authorId,
                Text = request.Text,
                CreatedAt = now,
                UpdatedAt = now,
            };
            ApplyWeatherSnapshot(say, request.Weather);
            ApplyLunar(say, now);

            // 绑定媒体（ticket 06）：校验存在/属主/未绑定 + 数量与类型规则。
            var mediaError = await BindMediaAsync(db, authorId, say.Id, request.MediaIds, ct);
            if (mediaError is not null)
            {
                return Results.BadRequest(new { error = mediaError });
            }

            foreach (var recipient in recipientsResult.Recipients)
            {
                db.ContentRecipients.Add(new ContentRecipient
                {
                    Id = Guid.NewGuid(),
                    ContentId = say.Id,
                    RecipientUserId = recipient.UserId,
                    AddedAt = now,
                });
                // 站内信（ticket 08）：发布即写 Mentioned；自己 @ 自己不产生。
                if (recipient.UserId != authorId)
                {
                    db.InboxMessages.Add(new InboxMessage
                    {
                        Id = Guid.NewGuid(),
                        UserId = recipient.UserId,
                        Type = InboxMessageType.Mentioned,
                        ContentId = say.Id,
                        CreatedAt = now,
                    });
                }
            }

            db.Contents.Add(say);
            await db.SaveChangesAsync(ct);

            var media = await LoadMediaAsync(db, say.Id, ct);
            var author = await LoadAuthorAsync(db, say.AuthorId, ct);
            return Results.Created($"/contents/{say.Id}", ToResponse(say, author, recipientsResult.Recipients, media, commentCount: 0));
        }).AddEndpointFilter(RequireInitialized);

        app.MapPost("/contents/journals", [Authorize] async (
            CreateJournalRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var recipientsResult = await ValidateRecipientsAsync(db, authorId, request.RecipientIds, ct);
            if (recipientsResult.Error is not null)
            {
                return Results.BadRequest(new { error = recipientsResult.Error });
            }

            var now = DateTime.UtcNow;
            var journal = new Journal
            {
                Id = Guid.NewGuid(),
                AuthorId = authorId,
                Title = request.Title,
                MarkdownBody = request.Markdown,
                CreatedAt = now,
                UpdatedAt = now,
            };
            ApplyWeatherSnapshot(journal, request.Weather);
            ApplyLunar(journal, now);

            foreach (var recipient in recipientsResult.Recipients)
            {
                db.ContentRecipients.Add(new ContentRecipient
                {
                    Id = Guid.NewGuid(),
                    ContentId = journal.Id,
                    RecipientUserId = recipient.UserId,
                    AddedAt = now,
                });
                // 站内信（ticket 08）：发布即写 Mentioned；自己 @ 自己不产生。
                if (recipient.UserId != authorId)
                {
                    db.InboxMessages.Add(new InboxMessage
                    {
                        Id = Guid.NewGuid(),
                        UserId = recipient.UserId,
                        Type = InboxMessageType.Mentioned,
                        ContentId = journal.Id,
                        CreatedAt = now,
                    });
                }
            }

            db.Contents.Add(journal);
            await db.SaveChangesAsync(ct);

            var author = await LoadAuthorAsync(db, journal.AuthorId, ct);
            return Results.Created($"/contents/{journal.Id}", ToResponse(journal, author, recipientsResult.Recipients, new List<MediaResponse>(), commentCount: 0));
        }).AddEndpointFilter(RequireInitialized);

        app.MapGet("/contents/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var viewerId = CurrentUserId(principal);
            var content = await contentQuery.FindVisibleAsync(id, viewerId, ct);
            if (content is null)
            {
                return Results.NotFound(new { error = "content not found" });
            }

            var recipients = (await db.ContentRecipients
                .Where(r => r.ContentId == id)
                .Select(r => new { r.RecipientUserId, r.User.Nickname })
                .ToListAsync(ct))
                .Select(r => new RecipientResponse(r.RecipientUserId, r.Nickname))
                .ToList();

            var media = await LoadMediaAsync(db, id, ct);
            var commentCount = await db.Comments.CountAsync(c => c.ContentId == id, ct);
            return Results.Ok(ToResponse(content, await LoadAuthorAsync(db, content.AuthorId, ct), recipients, media, commentCount));
        });

        app.MapPut("/contents/{id:guid}", [Authorize] async (
            Guid id,
            UpdateContentRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IContentQuery contentQuery,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (content is null || content.AuthorId != authorId)
            {
                // 非作者与不存在统一 404，不泄露存在性。
                return Results.NotFound(new { error = "content not found" });
            }

            var changed = false;

            if (content is Say && request.MediaIds is not null)
            {
                // 说说的媒体重绑：先解绑旧媒体（文件保留），再按创建同规则绑定。
                var bound = await db.MediaAssets.Where(m => m.ContentId == id).ToListAsync(ct);
                foreach (var asset in bound)
                {
                    asset.ContentId = null;
                }

                var mediaError = await BindMediaAsync(db, authorId, id, request.MediaIds, ct);
                if (mediaError is not null)
                {
                    return Results.BadRequest(new { error = mediaError });
                }

                changed = true;
            }

            if (content is Say say && request.Text is not null)
            {
                say.Text = request.Text;
                changed = true;
            }

            if (request.RecipientIds is not null)
            {
                // 可见名单整体替换（story 14）：校验全部为双向好友；新增者写名单 + Mentioned
                // 站内信（自己 @ 自己不产生）；被移除者删行即立即失权。
                var recipientsResult = await ValidateRecipientsAsync(db, authorId, request.RecipientIds, ct);
                if (recipientsResult.Error is not null)
                {
                    return Results.BadRequest(new { error = recipientsResult.Error });
                }

                var existingRows = await db.ContentRecipients
                    .Where(r => r.ContentId == id)
                    .ToListAsync(ct);
                var existingIds = existingRows.Select(r => r.RecipientUserId).ToHashSet();
                var targetIds = recipientsResult.Recipients.Select(r => r.UserId).ToHashSet();
                var recipientNow = DateTime.UtcNow;
                foreach (var recipient in recipientsResult.Recipients.Where(r => !existingIds.Contains(r.UserId)))
                {
                    db.ContentRecipients.Add(new ContentRecipient
                    {
                        Id = Guid.NewGuid(),
                        ContentId = id,
                        RecipientUserId = recipient.UserId,
                        AddedAt = recipientNow,
                    });
                    if (recipient.UserId != authorId)
                    {
                        db.InboxMessages.Add(new InboxMessage
                        {
                            Id = Guid.NewGuid(),
                            UserId = recipient.UserId,
                            Type = InboxMessageType.Mentioned,
                            ContentId = id,
                            CreatedAt = recipientNow,
                        });
                    }
                }

                foreach (var row in existingRows.Where(r => !targetIds.Contains(r.RecipientUserId)))
                {
                    db.ContentRecipients.Remove(row);
                }

                changed = true;
            }

            if (content is Journal journal)
            {
                if (request.Title is not null)
                {
                    journal.Title = request.Title;
                    changed = true;
                }

                if (request.MarkdownBody is not null)
                {
                    journal.MarkdownBody = request.MarkdownBody;
                    changed = true;
                }
            }

            if (request.Weather is not null)
            {
                ApplyWeatherSnapshot(content, request.Weather);
                changed = true;
            }

            if (changed)
            {
                content.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            var recipients = await LoadRecipientsAsync(db, id, ct);
            var media = await LoadMediaAsync(db, id, ct);
            var commentCount = await db.Comments.CountAsync(c => c.ContentId == id, ct);
            return Results.Ok(ToResponse(content, await LoadAuthorAsync(db, content.AuthorId, ct), recipients, media, commentCount));
        });

        app.MapDelete("/contents/{id:guid}", [Authorize] async (
            Guid id,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            var authorId = CurrentUserId(principal);
            var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (content is null || content.AuthorId != authorId)
            {
                return Results.NotFound(new { error = "content not found" });
            }

            // 级联清理在应用层：媒体文件在磁盘，必须先删行再删文件（PRD 9.4）。
            var assets = await db.MediaAssets.Where(m => m.ContentId == id).ToListAsync(ct);
            db.MediaAssets.RemoveRange(assets);
            db.Contents.Remove(content);
            // DB 级联：ContentRecipient / Comment / InboxMessage（FK 已配 Cascade）。
            await db.SaveChangesAsync(ct);

            foreach (var asset in assets)
            {
                await storage.DeleteAsync(asset.StoredName);
                if (asset.ThumbStoredName is not null)
                {
                    await storage.DeleteAsync(asset.ThumbStoredName);
                }
            }

            return Results.NoContent();
        });

        app.MapGet("/me/contents", [Authorize] async (
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            string? kind,
            string? date,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var kindError = ValidateKind(kind);
            if (kindError is not null)
            {
                return Results.BadRequest(new { error = kindError });
            }

            // 热力格点格下钻（homepage-v2 ticket 05）：date=yyyy-MM-dd → 中国区本地日的 UTC 区间。
            var dayFrom = default(DateTime?);
            var dayTo = default(DateTime?);
            if (date is not null)
            {
                if (!DateOnly.TryParseExact(
                        date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    return Results.BadRequest(new { error = "date must be yyyy-MM-dd" });
                }

                var zone = MeStatsEndpoints.ChinaZone();
                dayFrom = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
                dayTo = dayFrom.Value.AddDays(1);
            }

            var (skip, take) = NormalizePage(offset, limit);
            return await RunListQueryAsync(contentQuery, db, new ContentQuerySpec
            {
                ViewerId = me,
                AuthorId = me,
                Kind = kind,
                CreatedFrom = dayFrom,
                CreatedTo = dayTo,
                Offset = skip,
                Limit = take,
            }, ct);
        }).AddEndpointFilter(RequireInitialized);

        app.MapGet("/me/received", [Authorize] async (
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            string? kind,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var kindError = ValidateKind(kind);
            if (kindError is not null)
            {
                return Results.BadRequest(new { error = kindError });
            }

            var (skip, take) = NormalizePage(offset, limit);
            return await RunListQueryAsync(contentQuery, db, new ContentQuerySpec
            {
                ViewerId = me,
                RecipientId = me,
                ExcludeAuthorId = me,
                Kind = kind,
                Offset = skip,
                Limit = take,
            }, ct);
        }).AddEndpointFilter(RequireInitialized);

        app.MapGet("/users/{userId:guid}/contents", [Authorize] async (
            Guid userId,
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            string? kind,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var kindError = ValidateKind(kind);
            if (kindError is not null)
            {
                return Results.BadRequest(new { error = kindError });
            }

            var (skip, take) = NormalizePage(offset, limit);
            return await RunListQueryAsync(contentQuery, db, new ContentQuerySpec
            {
                ViewerId = me,
                AuthorId = userId,
                Kind = kind,
                Offset = skip,
                Limit = take,
            }, ct);
        }).AddEndpointFilter(RequireInitialized);

        app.MapGet("/search", [Authorize] async (
            ClaimsPrincipal principal,
            IContentQuery contentQuery,
            HZoneDbContext db,
            string q,
            string? kind,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var kindError = ValidateKind(kind);
            if (kindError is not null)
            {
                return Results.BadRequest(new { error = kindError });
            }

            var (skip, take) = NormalizePage(offset, limit);
            return await RunListQueryAsync(spec: new ContentQuerySpec
            {
                ViewerId = me,
                AuthorId = me,
                SearchText = q,
                Kind = kind,
                Offset = skip,
                Limit = take,
            }, contentQuery: contentQuery, db: db, ct: ct);
        }).AddEndpointFilter(RequireInitialized);
    }

    private static ContentResponse ToResponse(
        Content content,
        AuthorResponse author,
        List<RecipientResponse> recipients,
        List<MediaResponse> media,
        int commentCount)
    {
        return content switch
        {
            Say s => new ContentResponse(
                s.Id, "say", author, s.Text, null, null,
                recipients, s.CreatedAt, s.UpdatedAt, commentCount, media,
                WeatherOf(s), LunarOf(s)),
            Journal j => new ContentResponse(
                j.Id, "journal", author, null, j.Title, j.MarkdownBody,
                recipients, j.CreatedAt, j.UpdatedAt, commentCount, media,
                WeatherOf(j), LunarOf(j)),
            _ => throw new InvalidOperationException("unknown content type"),
        };
    }

    /// <summary>
    /// 农历三字段装配：旧内容（农历列空）由 CreatedAt 惰性推导，不回写库。
    /// </summary>
    private static LunarResponse LunarOf(Content content)
    {
        return string.IsNullOrEmpty(content.LunarGanzhiYear)
            ? FromComputed(LunarCalendar.Compute(new DateTimeOffset(content.CreatedAt, TimeSpan.Zero)))
            : new LunarResponse(content.LunarGanzhiYear, content.LunarMonthDay ?? string.Empty, content.LunarTerm ?? string.Empty);
    }

    private static LunarResponse FromComputed((string GanzhiYear, string MonthDay, string Term) lunar) =>
        new(lunar.GanzhiYear, lunar.MonthDay, lunar.Term);

    /// <summary>天气快照装配：无天气（WeatherCity 空）→ null（旧内容不回补）。</summary>
    private static WeatherResponse? WeatherOf(Content content) =>
        content.WeatherCity is null
            ? null
            : new WeatherResponse(content.WeatherCity, content.WeatherCondition, content.WeatherTempC);

    /// <summary>天气快照写入：整体替换。</summary>
    private static void ApplyWeatherSnapshot(Content content, WeatherSnapshotRequest? weather)
    {
        content.WeatherCity = weather?.City;
        content.WeatherCondition = weather?.Condition;
        content.WeatherTempC = weather?.TempC;
    }

    /// <summary>保存时计算并落库农历三字段（以 UTC 时间戳推中国区日期）。</summary>
    private static void ApplyLunar(Content content, DateTime utcNow)
    {
        (content.LunarGanzhiYear, content.LunarMonthDay, content.LunarTerm) =
            LunarCalendar.Compute(new DateTimeOffset(utcNow, TimeSpan.Zero));
    }

    private static async Task<AuthorResponse> LoadAuthorAsync(HZoneDbContext db, Guid userId, CancellationToken ct)
    {
        var nickname = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Nickname)
            .SingleAsync(ct);
        return new AuthorResponse(userId, nickname);
    }

    /// <summary>
    /// 列表/搜索共用出口：执行单一入口查询并批量装配条目 DTO
    /// （作者、收件人、媒体、评论数各一次批查询，避免逐条 N+1）。
    /// </summary>
    private static async Task<IResult> RunListQueryAsync(
        IContentQuery contentQuery, HZoneDbContext db, ContentQuerySpec spec, CancellationToken ct)
    {
        var (items, total) = await contentQuery.QueryAsync(spec, ct);
        var responses = await ToResponsesAsync(db, items, ct);
        return Results.Ok(new ContentListResponse(responses, total, spec.Offset, spec.Limit));
    }

    /// <summary>批量装配列表条目 DTO（与详情同形）。</summary>
    private static async Task<List<ContentResponse>> ToResponsesAsync(
        HZoneDbContext db, List<Content> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return new List<ContentResponse>();
        }

        var ids = items.Select(c => c.Id).ToList();
        var authorIds = items.Select(c => c.AuthorId).Distinct().ToList();

        var authors = await db.Users
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        var recipientsByContent = (await db.ContentRecipients
                .Where(r => ids.Contains(r.ContentId))
                .Select(r => new { r.ContentId, r.RecipientUserId, r.User.Nickname })
                .ToListAsync(ct))
            .GroupBy(r => r.ContentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new RecipientResponse(r.RecipientUserId, r.Nickname)).ToList());

        var mediaRows = await db.MediaAssets
            .Where(m => m.ContentId != null && ids.Contains(m.ContentId.Value))
            .ToListAsync(ct);
        var mediaByContent = mediaRows
            .GroupBy(m => m.ContentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(MediaResponses.ToResponse).ToList());

        var commentCounts = await db.Comments
            .Where(c => ids.Contains(c.ContentId))
            .GroupBy(c => c.ContentId)
            .Select(g => new { ContentId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var commentByContent = commentCounts.ToDictionary(x => x.ContentId, x => x.Count);

        return items.Select(c => new ContentResponse(
            c.Id,
            c is Say ? "say" : "journal",
            new AuthorResponse(c.AuthorId, authors[c.AuthorId]),
            c is Say s ? s.Text : null,
            c is Journal j ? j.Title : null,
            c is Journal j2 ? j2.MarkdownBody : null,
            recipientsByContent.GetValueOrDefault(c.Id) ?? new(),
            c.CreatedAt,
            c.UpdatedAt,
            commentByContent.GetValueOrDefault(c.Id),
            mediaByContent.GetValueOrDefault(c.Id) ?? new(),
            WeatherOf(c),
            LunarOf(c))).ToList();
    }

    /// <summary>kind 查询参数校验：say/journal/null 之外 → 错误消息。</summary>
    private static string? ValidateKind(string? kind) =>
        kind is null or "say" or "journal" ? null : "invalid kind";

    /// <summary>分页参数归一：offset 下限 0；limit 默认 20，上限 100。</summary>
    private static (int Offset, int Limit) NormalizePage(int? offset, int? limit) =>
        (Math.Max(0, offset ?? 0), limit is null or <= 0 ? 20 : Math.Min(limit.Value, 100));

    private static async Task<List<RecipientResponse>> LoadRecipientsAsync(
        HZoneDbContext db, Guid contentId, CancellationToken ct)
    {
        return (await db.ContentRecipients
                .Where(r => r.ContentId == contentId)
                .Select(r => new { r.RecipientUserId, r.User.Nickname })
                .ToListAsync(ct))
            .Select(r => new RecipientResponse(r.RecipientUserId, r.Nickname))
            .ToList();
    }

    private static async Task<List<MediaResponse>> LoadMediaAsync(
        HZoneDbContext db, Guid contentId, CancellationToken ct)
    {
        return (await db.MediaAssets
                .Where(m => m.ContentId == contentId)
                .ToListAsync(ct))
            .Select(MediaResponses.ToResponse)
            .ToList();
    }

    /// <summary>
    /// 校验并绑定媒体到内容：媒体须存在、属当前用户、未绑定；
    /// 数量与类型规则 = ≤9 张图 或 1 个视频/音频。返回错误消息或 null。
    /// </summary>
    private static async Task<string?> BindMediaAsync(
        HZoneDbContext db, Guid authorId, Guid contentId, List<Guid>? mediaIds, CancellationToken ct)
    {
        var ids = (mediaIds ?? new List<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var assets = await db.MediaAssets
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);
        if (assets.Count != ids.Count
            || assets.Any(m => m.OwnerUserId != authorId)
            || assets.Any(m => m.ContentId is not null))
        {
            return "invalid media ids";
        }

        var validShape = (assets.Count <= 9 && assets.All(m => m.Kind == MediaKind.Image))
            || (assets.Count == 1 && assets[0].Kind != MediaKind.Image);
        if (!validShape)
        {
            return "media count or kind violates say rules";
        }

        foreach (var asset in assets)
        {
            asset.ContentId = contentId;
        }

        return null;
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }

    private static async Task<RecipientsResult> ValidateRecipientsAsync(
        HZoneDbContext db,
        Guid authorId,
        List<Guid> recipientIds,
        CancellationToken ct)
    {
        var ids = (recipientIds ?? new List<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new RecipientsResult(new List<RecipientResponse>(), null);
        }

        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nickname })
            .ToListAsync(ct);
        if (users.Count != ids.Count)
        {
            return new RecipientsResult(new List<RecipientResponse>(), "recipient user not found");
        }

        var friendships = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Where(f => f.RequesterId == authorId || f.AddresseeId == authorId)
            .Select(f => new { f.RequesterId, f.AddresseeId })
            .ToListAsync(ct);
        var friendIds = friendships
            .Select(f => f.RequesterId == authorId ? f.AddresseeId : f.RequesterId)
            .ToList();
        if (ids.Any(id => !friendIds.Contains(id)))
        {
            return new RecipientsResult(new List<RecipientResponse>(), "recipient is not an accepted friend");
        }

        var recipients = users
            .Select(u => new RecipientResponse(u.Id, u.Nickname))
            .ToList();
        return new RecipientsResult(recipients, null);
    }
}
