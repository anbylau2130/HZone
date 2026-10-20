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

    private async Task SeedSayAsync(Guid authorId, int daysAgo)
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
