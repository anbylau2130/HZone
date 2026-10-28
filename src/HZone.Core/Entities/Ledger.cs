namespace HZone.Core.Entities;

/// <summary>
/// 账本：个人账本（每人一张，惰性建，不可删）+ 共同账本（拉好友共记）。
/// </summary>
public class Ledger
{
    public Guid Id { get; set; }

    /// <summary>创建人（个人账本即本人）。</summary>
    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>personal = 个人账本（仅本人）；shared = 共同账本（成员共记共阅）。</summary>
    public bool IsShared { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<LedgerMember> Members { get; set; } = [];

    public List<LedgerEntry> Entries { get; set; } = [];
}

/// <summary>共同账本成员（个人账本无成员行）。</summary>
public class LedgerMember
{
    public Guid Id { get; set; }

    public Guid LedgerId { get; set; }

    public Guid UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public Ledger Ledger { get; set; } = null!;

    public User User { get; set; } = null!;
}

/// <summary>账单条目：金额存分（避免 SQLite decimal 排序/精度坑），收支二分类。</summary>
public class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid LedgerId { get; set; }

    /// <summary>记录人（共同账本里区分谁记的）。</summary>
    public Guid RecorderUserId { get; set; }

    /// <summary>金额（分，恒正；方向由 Kind 决定）。</summary>
    public long AmountCents { get; set; }

    /// <summary>expense | income。</summary>
    public LedgerEntryKind Kind { get; set; }

    /// <summary>分类 key（内置分类表驱动前端图标/文案；自定义分类暂缓）。</summary>
    public string Category { get; set; } = null!;

    /// <summary>备注（≤200）。</summary>
    public string? Note { get; set; }

    /// <summary>记账日期（自然日；月度查询口径）。</summary>
    public DateOnly EntryDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public Ledger Ledger { get; set; } = null!;

    public User Recorder { get; set; } = null!;
}

public enum LedgerEntryKind
{
    Expense = 0,
    Income = 1
}
