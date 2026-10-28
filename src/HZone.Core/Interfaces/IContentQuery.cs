namespace HZone.Core.Interfaces;

using HZone.Core.Entities;

/// <summary>
/// 列表/搜索查询参数对象：全部走 VisibleTo 作为可见性单一入口，调用方只叠加过滤条件。
/// </summary>
public sealed record ContentQuerySpec
{
    /// <summary>观察者（可见性基线 VisibleTo 的主体）。</summary>
    public required Guid ViewerId { get; init; }

    /// <summary>按作者过滤。</summary>
    public Guid? AuthorId { get; init; }

    /// <summary>排除该作者的内容（配合 RecipientId 实现"我被 @ 且非我本人所发"）。</summary>
    public Guid? ExcludeAuthorId { get; init; }

    /// <summary>按收件人过滤（Recipients 含此人，即"被 @"）。</summary>
    public Guid? RecipientId { get; init; }

    /// <summary>"say" | "journal" | null（全部）。非法值由端点先行校验。</summary>
    public string? Kind { get; init; }

    /// <summary>LIKE 关键词：命中说说文本 / 日志标题+正文。</summary>
    public string? SearchText { get; init; }

    /// <summary>按标签过滤（ContentTags 含该标签的内容，可与关键词叠加）。</summary>
    public string? Tag { get; init; }

    /// <summary>CreatedAt UTC 下界（含）——热力格点格下钻按中国区本地日取 UTC 区间；
    /// 首页时间线也用它做"最近一周"首屏窗口。</summary>
    public DateTime? CreatedFrom { get; init; }

    /// <summary>CreatedAt UTC 上界（不含）。</summary>
    public DateTime? CreatedTo { get; init; }

    /// <summary>
    /// 键集游标（与 CreatedAt 倒序 + Id 倒序的稳定排序配套）：返回
    /// (CreatedAt, Id) 字典序严格小于游标的内容——同秒/同毫秒连发的时间线
    /// 翻页不丢条目（朴素时间 before 会永久漏掉并列时间的项）。
    /// </summary>
    public DateTime? CursorCreatedAt { get; init; }

    public Guid? CursorId { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; }
}

public interface IContentQuery
{
    IQueryable<Content> VisibleTo(Guid viewerId);

    Task<Content?> FindVisibleAsync(Guid contentId, Guid viewerId, CancellationToken ct = default);

    /// <summary>
    /// 列表单一入口：时间倒序分页。total 为过滤后（分页前）总数。
    /// </summary>
    Task<(List<Content> Items, int Total)> QueryAsync(ContentQuerySpec spec, CancellationToken ct = default);
}
