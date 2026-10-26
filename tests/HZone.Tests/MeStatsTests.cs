using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

public class MeStatsTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    /// <summary>
    /// 中国区「今天」（与后端 MeStatsEndpoints.ChinaZone 同口径）。测试宿主时区不限，
    /// 种子日期与断言必须用北京日界——否则北京 00:00–08:00 窗口（UTC 与北京跨日）内
    /// UTC 参考系全部错位（实测踩中：本机 GMT 时区下午挂、上午过）。
    /// </summary>
    private static readonly TimeZoneInfo ChinaZone =
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private static DateTime CnToday() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.Now, ChinaZone).Date;

    public MeStatsTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Stats_returns_totals_streak_and_today_lunar()
    {
        var author = await _factory.SeedUserAsync("ms-a@test.local", "MsA");
        var friend = await _factory.SeedUserAsync("ms-b@test.local", "MsB");
        await _factory.SeedFriendshipAsync(author, friend);
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 三天前 / 昨天 / 今天各发一条（04:00 UTC = 中国区当日 12:00，跨日安全）；
        // 三天前与昨天-今天之间存在空档 → streak 应为 2。
        await SeedSayAsync(author.Id, daysAgo: 3);
        await SeedJournalAsync(author.Id, daysAgo: 1);
        await SeedSayAsync(author.Id, daysAgo: 0);

        var res = await client.GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var totals = body.GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("says").GetInt32());
        Assert.Equal(1, totals.GetProperty("journals").GetInt32());
        Assert.Equal(1, totals.GetProperty("friends").GetInt32());
        Assert.Equal(2, body.GetProperty("streak").GetInt32());

        var today = body.GetProperty("today");
        Assert.NotEmpty(today.GetProperty("ganzhiYear").GetString()!);
        Assert.NotEmpty(today.GetProperty("monthDay").GetString()!);
    }

    [Fact]
    public async Task Stats_without_content_has_zero_streak()
    {
        var author = await _factory.SeedUserAsync("ms-c@test.local", "MsC");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var res = await client.GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var totals = body.GetProperty("totals");
        Assert.Equal(0, totals.GetProperty("says").GetInt32());
        Assert.Equal(0, totals.GetProperty("journals").GetInt32());
        Assert.Equal(0, totals.GetProperty("friends").GetInt32());
        Assert.Equal(0, body.GetProperty("streak").GetInt32());
        // 农历三字段照常返回（前端今日卡不实现农历算法）。
        Assert.NotEmpty(body.GetProperty("today").GetProperty("monthDay").GetString()!);
    }

    [Fact]
    public async Task Stats_requires_auth()
    {
        var res = await _factory.CreateClient().GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Stats_heatmap_zero_filled_90_days_with_counts()
    {
        var author = await _factory.SeedUserAsync("ms-d@test.local", "MsD");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 窗口内：今天 2 条 + 89 天前 1 条；窗口外：200 天前 1 条不计入。
        await SeedSayAsync(author.Id, daysAgo: 0);
        await SeedSayAsync(author.Id, daysAgo: 0, weatherCity: "上海");
        await SeedSayAsync(author.Id, daysAgo: 89, weatherCity: "杭州");
        await SeedSayAsync(author.Id, daysAgo: 200);

        var res = await client.GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var heatmap = body.GetProperty("heatmap");
        Assert.Equal(90, heatmap.GetArrayLength());

        // 首格 = 89 天前（窗口最旧一天），末格 = 今天；今天计数 2，200 天前不计入。
        var first = heatmap[0];
        var last = heatmap[89];
        Assert.Equal(CnToday().AddDays(-89).ToString("yyyy-MM-dd"), first.GetProperty("date").GetString());
        Assert.Equal(1, first.GetProperty("count").GetInt32());
        Assert.Equal(CnToday().ToString("yyyy-MM-dd"), last.GetProperty("date").GetString());
        Assert.Equal(2, last.GetProperty("count").GetInt32());

        // 窗口内计数总和 = 3（窗口外的 200 天前内容不计入）。
        var sum = heatmap.EnumerateArray().Sum(c => c.GetProperty("count").GetInt32());
        Assert.Equal(3, sum);
    }

    [Fact]
    public async Task Stats_footprints_consecutive_dedup_and_skip_no_weather()
    {
        var author = await _factory.SeedUserAsync("ms-e@test.local", "MsE");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 时间正序：杭州(7d) → 杭州(6d) → 上海(5d) → 无天气(3d) → 上海(1d) → 杭州(today)。
        // 有城市序列 = 杭州,杭州,上海,上海,杭州 → 同城连续去重后 3 条：杭州→上海→杭州，
        // 倒序返回（最近在上），firstAt 取每段首次出现的时刻。
        await SeedSayAsync(author.Id, daysAgo: 7, weatherCity: "杭州");
        await SeedSayAsync(author.Id, daysAgo: 6, weatherCity: "杭州");
        await SeedSayAsync(author.Id, daysAgo: 5, weatherCity: "上海");
        await SeedSayAsync(author.Id, daysAgo: 3);
        await SeedSayAsync(author.Id, daysAgo: 1, weatherCity: "上海");
        await SeedSayAsync(author.Id, daysAgo: 0, weatherCity: "杭州");

        var res = await client.GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var footprints = body.GetProperty("footprints");
        Assert.Equal(3, footprints.GetArrayLength());

        var fp = footprints.EnumerateArray().ToArray();
        Assert.Equal("杭州", fp[0].GetProperty("city").GetString());
        Assert.Equal("上海", fp[1].GetProperty("city").GetString());
        Assert.Equal("杭州", fp[2].GetProperty("city").GetString());

        // firstAt = 每城市段首次出现时刻（今天/5 天前/7 天前，种子固定 04:00 UTC）。
        var today = CnToday();
        Assert.Equal(today.ToString("yyyy-MM-dd"), fp[0].GetProperty("firstAt").GetString()![..10]);
        Assert.Equal(today.AddDays(-5).ToString("yyyy-MM-dd"), fp[1].GetProperty("firstAt").GetString()![..10]);
        Assert.Equal(today.AddDays(-7).ToString("yyyy-MM-dd"), fp[2].GetProperty("firstAt").GetString()![..10]);
    }

    [Fact]
    public async Task MeContents_filters_by_date_when_given()
    {
        var author = await _factory.SeedUserAsync("ms-f@test.local", "MsF");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        await SeedSayAsync(author.Id, daysAgo: 0);
        await SeedSayAsync(author.Id, daysAgo: 1);

        // 无 date 参数 = 原行为（全部）。
        var all = await client.GetAsync("/me/contents");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var allBody = await ReadJson(all);
        Assert.Equal(2, allBody.GetProperty("total").GetInt32());

        // date=今天 → 只剩今天的 1 条；date 格式非法 → 400。
        var today = await client.GetAsync($"/me/contents?date={CnToday():yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
        var todayBody = await ReadJson(today);
        Assert.Equal(1, todayBody.GetProperty("total").GetInt32());

        var bad = await client.GetAsync("/me/contents?date=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task OnThisDay_returns_past_years_same_month_day_only()
    {
        var author = await _factory.SeedUserAsync("ms-otd@test.local", "MsOtd");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 往年今天：2 条（say 2 年前 / journal 1 年前）；今年今天 1 条不计入；昨天去年 1 条不计入。
        await SeedOnThisDaySayAsync(author.Id, yearsAgo: 2, text: "两年前的今天");
        await SeedOnThisDayJournalAsync(author.Id, yearsAgo: 1, title: "一年前的今天");
        await SeedOnThisDaySayAsync(author.Id, yearsAgo: 0, text: "今年的今天不匹配");
        await SeedOnThisDaySayAsync(author.Id, yearsAgo: 1, dayOffset: -1, text: "去年昨天不匹配");

        var res = await client.GetAsync("/me/on-this-day");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(2, body.GetArrayLength());

        var items = body.EnumerateArray().ToArray();
        // 倒序（最近年份在上），say 给 summary、journal 给 title。
        var thisYear = DateTime.UtcNow.Year;
        Assert.Equal(thisYear - 1, items[0].GetProperty("year").GetInt32());
        Assert.Equal("journal", items[0].GetProperty("kind").GetString());
        Assert.Equal("一年前的今天", items[0].GetProperty("title").GetString());
        Assert.True(items[0].GetProperty("summary").ValueKind == JsonValueKind.Null);
        Assert.Equal(thisYear - 2, items[1].GetProperty("year").GetInt32());
        Assert.Equal("say", items[1].GetProperty("kind").GetString());
        Assert.Equal("两年前的今天", items[1].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task OnThisDay_empty_when_no_history()
    {
        var author = await _factory.SeedUserAsync("ms-otd-e@test.local", "MsOtdE");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var res = await client.GetAsync("/me/on-this-day");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await ReadJson(res)).GetArrayLength());
    }

    [Fact]
    public async Task InteractionSummary_counts_comments_and_mentions_within_7_days()
    {
        var author = await _factory.SeedUserAsync("ms-int@test.local", "MsInt");
        var friend = await _factory.SeedUserAsync("ms-int-b@test.local", "MsIntB");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var sayId = await SeedSayAsync(author.Id, daysAgo: 0);
        var commentNow = await SeedCommentAsync(friend.Id, sayId, daysAgo: 0);
        var commentOld = await SeedCommentAsync(friend.Id, sayId, daysAgo: 8);
        await SeedMentionAsync(author.Id, daysAgo: 0);
        await SeedMentionAsync(author.Id, daysAgo: 8);
        Assert.NotEqual(commentNow, commentOld);

        var res = await client.GetAsync("/me/interactions/summary");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(1, body.GetProperty("comments").GetInt32());
        Assert.Equal(1, body.GetProperty("mentions").GetInt32());
    }

    [Fact]
    public async Task Stats_monthly_counts_this_month_by_china_zone()
    {
        var author = await _factory.SeedUserAsync("ms-mon@test.local", "MsMon");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 本月：say×2（杭州·晴 / 上海·晴）+ journal×1（无天气）；去年 1 条不计入。
        await SeedSayAsync(author.Id, daysAgo: 0, weatherCity: "杭州", weatherCondition: "sunny");
        await SeedSayAsync(author.Id, daysAgo: 1, weatherCity: "上海", weatherCondition: "sunny");
        await SeedJournalAsync(author.Id, daysAgo: 2);
        await SeedOldYearSayAsync(author.Id, weatherCity: "杭州");

        var res = await client.GetAsync("/me/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var monthly = body.GetProperty("monthly");

        // 用与后端相同的时区口径计算期望值（月初边界稳健：跨月种子不计入）。
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, zone);
        var inMonth = new[] { 0, 1, 2 }
            .Select(d => TimeZoneInfo.ConvertTime(DateTimeOffset.Now.AddDays(-d), zone))
            .Count(d => d.Year == nowLocal.Year && d.Month == nowLocal.Month);

        Assert.Equal(inMonth >= 2 ? 2 : inMonth, monthly.GetProperty("says").GetInt32());
        Assert.Equal(inMonth >= 3 ? 1 : 0, monthly.GetProperty("journals").GetInt32());
        Assert.Equal(inMonth >= 2 ? 2 : inMonth, monthly.GetProperty("cities").GetInt32());

        // 两个晴天 > 雨天无 → sunny 最常见；无天气的旧内容不回补。
        var expectedCondition = inMonth >= 1 ? "sunny" : (string?)null;
        Assert.Equal(expectedCondition, monthly.GetProperty("topCondition").GetString());
    }

    /// <summary>去年今天/往日的随记（on-this-day 专用，年月日按目标年份固定 04:00 UTC）。</summary>
    private async Task SeedOnThisDaySayAsync(Guid authorId, int yearsAgo, string text, int dayOffset = 0)
    {
        var today = CnToday();
        var day = today.AddYears(-yearsAgo).AddDays(dayOffset);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Text = text,
            CreatedAt = day.AddHours(4),
            UpdatedAt = day,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedOnThisDayJournalAsync(Guid authorId, int yearsAgo, string title)
    {
        var today = CnToday();
        var day = today.AddYears(-yearsAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Journal
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Title = title,
            MarkdownBody = "body",
            CreatedAt = day.AddHours(4),
            UpdatedAt = day,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedOldYearSayAsync(Guid authorId, string? weatherCity = null)
    {
        var day = CnToday().AddYears(-1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Text = "old year",
            CreatedAt = day.AddHours(4),
            UpdatedAt = day,
            WeatherCity = weatherCity,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCommentAsync(Guid authorId, Guid contentId, int daysAgo)
    {
        var created = DateTime.UtcNow.AddDays(-daysAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            ContentId = contentId,
            AuthorId = authorId,
            Text = "nice",
            CreatedAt = new DateTime(created.Year, created.Month, created.Day, 4, 0, 0, DateTimeKind.Utc),
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        return comment.Id;
    }

    private async Task SeedMentionAsync(Guid userId, int daysAgo)
    {
        var created = DateTime.UtcNow.AddDays(-daysAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.InboxMessages.Add(new InboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = InboxMessageType.Mentioned,
            CreatedAt = new DateTime(created.Year, created.Month, created.Day, 4, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedSayAsync(Guid authorId, int daysAgo, string? weatherCity = null, string? weatherCondition = null)
    {
        var created = DateTimeUtcDaysAgo(daysAgo);
        var sayId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = sayId,
            AuthorId = authorId,
            Text = $"streak {daysAgo}",
            CreatedAt = DateTimeUtcDaysAgo(daysAgo),
            UpdatedAt = created,
            WeatherCity = weatherCity,
            WeatherCondition = weatherCondition,
        });
        await db.SaveChangesAsync();
        return sayId;
    }

    private static DateTime DateTimeUtcDaysAgo(int daysAgo)
    {
        var d = CnToday().AddDays(-daysAgo);
        return new DateTime(d.Year, d.Month, d.Day, 4, 0, 0, DateTimeKind.Utc);
    }

    private async Task SeedJournalAsync(Guid authorId, int daysAgo)
    {
        // 与 SeedSayAsync 同口径：中国区日界 + 04:00 UTC（北京当日 12:00，跨日安全）
        var created = DateTimeUtcDaysAgo(daysAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Journal
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Title = "streak journal",
            MarkdownBody = "body",
            CreatedAt = created,
            UpdatedAt = created,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()).RootElement.Clone();
    }
}
