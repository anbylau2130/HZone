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
        Assert.Equal(DateTime.UtcNow.AddDays(-89).Date.ToString("yyyy-MM-dd"), first.GetProperty("date").GetString());
        Assert.Equal(1, first.GetProperty("count").GetInt32());
        Assert.Equal(DateTime.UtcNow.Date.ToString("yyyy-MM-dd"), last.GetProperty("date").GetString());
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
        var today = DateTime.UtcNow.Date;
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
        var today = await client.GetAsync($"/me/contents?date={DateTime.UtcNow.Date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
        var todayBody = await ReadJson(today);
        Assert.Equal(1, todayBody.GetProperty("total").GetInt32());

        var bad = await client.GetAsync("/me/contents?date=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    private async Task SeedSayAsync(Guid authorId, int daysAgo, string? weatherCity = null)
    {
        var created = DateTime.UtcNow.AddDays(-daysAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Text = $"streak {daysAgo}",
            CreatedAt = new DateTime(created.Year, created.Month, created.Day, 4, 0, 0, DateTimeKind.Utc),
            UpdatedAt = created,
            WeatherCity = weatherCity,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedJournalAsync(Guid authorId, int daysAgo)
    {
        var created = DateTime.UtcNow.AddDays(-daysAgo);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Journal
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Title = "streak journal",
            MarkdownBody = "body",
            CreatedAt = new DateTime(created.Year, created.Month, created.Day, 4, 0, 0, DateTimeKind.Utc),
            UpdatedAt = created,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()).RootElement.Clone();
    }
}
