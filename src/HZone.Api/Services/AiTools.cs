using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Services;

/// <summary>
/// AI 内置工具执行器：HZone 数据能力的读写出口，供 AI 对话（function calling）
/// 与对外 MCP Server（同一套实现）共用。结果统一返回字符串（JSON/文本摘要）。
/// </summary>
public static class AiTools
{
    // 中文等非 ASCII 不转义（工具结果可读、断言可匹配）
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private static string Ser(object o) => JsonSerializer.Serialize(o, JsonOpts);

    // ---------- 工具定义（OpenAI function calling schema，MCP 对外同源） ----------

    public static readonly (string Name, string Description, JsonElement Parameters)[] Definitions =
    [
        Tool("search_content",
            "搜索用户自己的和收到分享的随记/笔记/评论，返回命中片段列表",
            """{"type":"object","properties":{"q":{"type":"string","description":"关键词"},"kind":{"type":"string","enum":["say","journal","comment"],"description":"可选类型过滤"}},"required":["q"]}"""),
        Tool("read_journal",
            "按 id 读取一篇笔记的标题与正文（markdown，超长截断）",
            """{"type":"object","properties":{"id":{"type":"string","description":"笔记 id（UUID）"}},"required":["id"]}"""),
        Tool("get_stats",
            "获取用户的记录统计：随记/笔记总数、连续记录天数、本月概要",
            """{"type":"object","properties":{},"required":[]}"""),
        Tool("get_ledger_summary",
            "获取记账月度汇总：支出/收入/分类占比（默认个人账本当月）",
            """{"type":"object","properties":{"month":{"type":"string","description":"yyyy-MM，缺省当月"}},"required":[]}"""),
        Tool("add_ledger_entry",
            "帮用户记一笔账（写入个人账本）",
            """{"type":"object","properties":{"amount":{"type":"number","description":"金额（元）"},"kind":{"type":"string","enum":["expense","income"]},"category":{"type":"string","description":"分类 key：food/transport/shopping/daily/fun/medical/home/otherExpense/salary/invest/bonus/otherIncome"},"note":{"type":"string","description":"备注"}},"required":["amount","kind","category"]}"""),
        Tool("create_journal",
            "为用户创建一篇笔记（markdown），返回新笔记 id",
            """{"type":"object","properties":{"title":{"type":"string"},"markdown":{"type":"string"}},"required":["title","markdown"]}"""),
    ];

    private static (string, string, JsonElement) Tool(string name, string desc, string schema) =>
        (name, desc, JsonDocument.Parse(schema).RootElement.Clone());

    /// <summary>OpenAI tools 数组（function calling 格式）。</summary>
    public static List<object> OpenAiTools(string? prefix = null) =>
        Definitions.Select(d => (object)new
        {
            type = "function",
            function = new
            {
                name = prefix is null ? d.Name : $"{prefix}{d.Name}",
                description = d.Description,
                parameters = d.Parameters,
            }
        }).ToList();

    // ---------- 执行 ----------

    public static async Task<string> ExecuteAsync(
        HZoneDbContext db, Guid userId, string name, JsonElement args, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "search_content" => await SearchContentAsync(db, userId, args, ct),
                "read_journal" => await ReadJournalAsync(db, userId, args, ct),
                "get_stats" => await GetStatsAsync(db, userId, ct),
                "get_ledger_summary" => await GetLedgerSummaryAsync(db, userId, args, ct),
                "add_ledger_entry" => await AddLedgerEntryAsync(db, userId, args, ct),
                "create_journal" => await CreateJournalAsync(db, userId, args, ct),
                _ => Ser(new { error = $"unknown tool: {name}" }),
            };
        }
        catch (Exception ex)
        {
            return Ser(new { error = ex.Message });
        }
    }

    private static async Task<string> SearchContentAsync(
        HZoneDbContext db, Guid userId, JsonElement args, CancellationToken ct)
    {
        var q = args.GetProperty("q").GetString() ?? "";
        var pattern = "%" + EscapeLike(q) + "%";
        var says = await db.Says
            .Where(c => (c.AuthorId == userId || c.Recipients.Any(r => r.RecipientUserId == userId))
                && EF.Functions.Like(c.Text, pattern, "\\"))
            .OrderByDescending(c => c.CreatedAt).Take(8)
            .Select(c => new { c.Id, c.Text, c.CreatedAt, Mine = c.AuthorId == userId })
            .ToListAsync(ct);
        var journals = await db.Journals
            .Where(c => (c.AuthorId == userId || c.Recipients.Any(r => r.RecipientUserId == userId))
                && (EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.MarkdownBody, pattern, "\\")))
            .OrderByDescending(c => c.CreatedAt).Take(8)
            .Select(c => new { c.Id, c.Title, Chars = c.MarkdownBody.Length, c.CreatedAt, Mine = c.AuthorId == userId })
            .ToListAsync(ct);
        return Ser(new
        {
            says = says.Select(s => new
            {
                s.Id, s.Mine,
                Snippet = s.Text.Length > 120 ? s.Text[..120] + "…" : s.Text,
                s.CreatedAt,
            }),
            journals = journals.Select(j => new { j.Id, j.Mine, j.Title, j.Chars, j.CreatedAt }),
        });
    }

    private static async Task<string> ReadJournalAsync(
        HZoneDbContext db, Guid userId, JsonElement args, CancellationToken ct)
    {
        if (!Guid.TryParse(args.GetProperty("id").GetString(), out var id))
        {
            return """{"error":"invalid id"}""";
        }
        var journal = await db.Journals.AsNoTracking()
            .Where(c => c.Id == id && (c.AuthorId == userId || c.Recipients.Any(r => r.RecipientUserId == userId)))
            .Select(c => new { c.Title, c.MarkdownBody, c.CreatedAt })
            .FirstOrDefaultAsync(ct);
        if (journal is null)
        {
            return """{"error":"journal not found"}""";
        }
        var body = journal.MarkdownBody.Length > 8000
            ? journal.MarkdownBody[..8000] + "\n…(截断)"
            : journal.MarkdownBody;
        return Ser(new { journal.Title, Body = body, journal.CreatedAt });
    }

    private static async Task<string> GetStatsAsync(HZoneDbContext db, Guid userId, CancellationToken ct)
    {
        var says = await db.Says.CountAsync(c => c.AuthorId == userId, ct);
        var journals = await db.Journals.CountAsync(c => c.AuthorId == userId, ct);
        // 连续天数：中国区自然日
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date);
        var days = await db.Contents.Where(c => c.AuthorId == userId)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var daySet = days.Select(d =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(d, TimeSpan.Zero), zone).Date))
            .ToHashSet();
        var streak = 0;
        for (var d = today; daySet.Contains(d); d = d.AddDays(-1)) streak++;
        return Ser(new { says, journals, streakDays = streak });
    }

    private static async Task<string> GetLedgerSummaryAsync(
        HZoneDbContext db, Guid userId, JsonElement args, CancellationToken ct)
    {
        var ledger = await db.Ledgers
            .Where(l => l.OwnerUserId == userId && !l.IsShared)
            .FirstOrDefaultAsync(ct);
        if (ledger is null)
        {
            return """{"expenseCents":0,"incomeCents":0,"byCategory":[]}""";
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date);
        DateOnly from;
        if (args.TryGetProperty("month", out var m) && m.GetString() is { Length: > 0 } monthStr
            && DateOnly.TryParseExact(monthStr, "yyyy-MM", out var parsed))
        {
            from = parsed;
        }
        else
        {
            from = new DateOnly(today.Year, today.Month, 1);
        }
        var to = from.AddMonths(1);
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.LedgerId == ledger.Id && x.EntryDate >= from && x.EntryDate < to)
            .Select(x => new { x.AmountCents, Kind = x.Kind == LedgerEntryKind.Expense ? "expense" : "income", x.Category })
            .ToListAsync(ct);
        return Ser(new
        {
            month = from.ToString("yyyy-MM"),
            expenseCents = entries.Where(x => x.Kind == "expense").Sum(x => x.AmountCents),
            incomeCents = entries.Where(x => x.Kind == "income").Sum(x => x.AmountCents),
            byCategory = entries.Where(x => x.Kind == "expense")
                .GroupBy(x => x.Category)
                .Select(g => new { category = g.Key, cents = g.Sum(x => x.AmountCents) })
                .OrderByDescending(g => g.cents),
        });
    }

    private static async Task<string> AddLedgerEntryAsync(
        HZoneDbContext db, Guid userId, JsonElement args, CancellationToken ct)
    {
        var amount = args.GetProperty("amount").GetDouble();
        var cents = (long)Math.Round(amount * 100);
        var kindStr = args.GetProperty("kind").GetString();
        var category = args.GetProperty("category").GetString();
        if (cents <= 0 || cents > 100_000_000_00 || category is null or { Length: 0 or > 32 }
            || (kindStr != "expense" && kindStr != "income"))
        {
            return """{"error":"invalid entry"}""";
        }
        var ledger = await db.Ledgers
            .Where(l => l.OwnerUserId == userId && !l.IsShared)
            .FirstOrDefaultAsync(ct);
        if (ledger is null)
        {
            ledger = new Ledger
            {
                Id = Guid.NewGuid(), OwnerUserId = userId, Name = "我的账本", IsShared = false,
                CreatedAt = DateTime.UtcNow,
            };
            db.Ledgers.Add(ledger);
            await db.SaveChangesAsync(ct);
        }
        string? note = args.TryGetProperty("note", out var n) ? n.GetString() : null;
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(),
            LedgerId = ledger.Id,
            RecorderUserId = userId,
            AmountCents = cents,
            Kind = kindStr == "expense" ? LedgerEntryKind.Expense : LedgerEntryKind.Income,
            Category = category,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
        };
        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ser(new { ok = true, entry.Id, entry.AmountCents });
    }

    private static async Task<string> CreateJournalAsync(
        HZoneDbContext db, Guid userId, JsonElement args, CancellationToken ct)
    {
        var title = args.GetProperty("title").GetString()?.Trim();
        var markdown = args.GetProperty("markdown").GetString() ?? "";
        if (string.IsNullOrEmpty(title) || title.Length > 200 || markdown.Length > 200_000)
        {
            return """{"error":"invalid title or markdown"}""";
        }
        var journal = new Journal
        {
            Id = Guid.NewGuid(),
            AuthorId = userId,
            Title = title,
            MarkdownBody = markdown,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Contents.Add(journal);
        await db.SaveChangesAsync(ct);
        return Ser(new { ok = true, journal.Id });
    }

    private static string EscapeLike(string input) => input
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
