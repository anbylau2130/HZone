using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 年度报告聚合：年份口径（中国区）、篇数/天数/连续/城市/天气/深夜/标签/
/// 收到的互动/最长笔记/年度高光。
/// </summary>
public class ReportTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public ReportTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Report_aggregates_the_year_in_cn_calendar()
    {
        var user = await _factory.SeedUserAsync("rep1@test.local", "Rep1");
        var fan = await _factory.SeedUserAsync("rep2@test.local", "Rep2");
        var fan2 = await _factory.SeedUserAsync("rep4@test.local", "Rep4");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var today = CnToday();
        // 2026：连续三天（d-2/d-1/d0）+ 更早孤日 d-10；一条深夜（23:30 CN）；两城两天气；标签×2；最长笔记
        // （普通条目固定 12:00 CN——凌晨 4 点也算"深夜"口径，别踩）
        var ids = new List<Guid>();
        ids.Add(await SeedSayAsync(user.Id, today.AddDays(-2), 12, "郑州", "cloudy", "晚安"));
        ids.Add(await SeedSayAsync(user.Id, today.AddDays(-1), 12, "郑州", "cloudy", "晚安"));
        var hotSay = await SeedSayAsync(user.Id, today, 23.5, "上海", "rain", "雨夜");
        ids.Add(hotSay);
        ids.Add(await SeedSayAsync(user.Id, today.AddDays(-10), 12, null, null, null));
        var journalId = await SeedJournalAsync(user.Id, today.AddDays(-1), new string('字', 1200), "长文");

        // 去年一条（不入 2026 报告，但出现在候选年份）
        await SeedSayAsync(user.Id, today.AddYears(-1), 12, "北京", null, null);

        // 互动：他人给两条随记点赞、给一条评论
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.ContentLikes.Add(new ContentLike
            {
                Id = Guid.NewGuid(), ContentId = hotSay, UserId = fan.Id, CreatedAt = DateTime.UtcNow
            });
            db.ContentLikes.Add(new ContentLike
            {
                Id = Guid.NewGuid(), ContentId = hotSay, UserId = fan2.Id, CreatedAt = DateTime.UtcNow
            });
            db.ContentLikes.Add(new ContentLike
            {
                Id = Guid.NewGuid(), ContentId = ids[0], UserId = fan.Id, CreatedAt = DateTime.UtcNow
            });
            db.Comments.Add(new Comment
            {
                Id = Guid.NewGuid(), ContentId = hotSay, AuthorId = fan.Id, Text = "赞", CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var year = today.Year;
        var res = await client.GetFromJsonAsync<JsonElement>($"/me/report?year={year}");
        Assert.Equal(year, res.GetProperty("year").GetInt32());
        Assert.Equal(4, res.GetProperty("sayCount").GetInt32());
        Assert.Equal(1, res.GetProperty("journalCount").GetInt32());
        Assert.Equal(4, res.GetProperty("recordedDays").GetInt32()); // d-10, d-2, d-1, d0
        Assert.Equal(3, res.GetProperty("longestStreak").GetInt32()); // d-2→d0

        var cities = res.GetProperty("cities").EnumerateArray().ToList();
        Assert.Equal(2, cities.Count);
        Assert.Equal("郑州", cities[0].GetProperty("city").GetString());
        Assert.Equal(2, cities[0].GetProperty("count").GetInt32());

        var weather = res.GetProperty("weather").EnumerateArray().ToList();
        Assert.Equal("cloudy", weather[0].GetProperty("condition").GetString());
        Assert.Equal(2, weather[0].GetProperty("count").GetInt32());

        Assert.Equal(1, res.GetProperty("nightCount").GetInt32()); // 23:30 CN 那条
        Assert.NotNull(res.GetProperty("latestAt").GetString());

        var tags = res.GetProperty("tags").EnumerateArray().ToList();
        Assert.Equal("晚安", tags[0].GetProperty("tag").GetString());
        Assert.Equal(2, tags[0].GetProperty("count").GetInt32());

        Assert.Equal(3, res.GetProperty("likesReceived").GetInt32());
        Assert.Equal(1, res.GetProperty("commentsReceived").GetInt32());

        var longest = res.GetProperty("longestJournal");
        Assert.Equal(journalId, longest.GetProperty("id").GetGuid());
        Assert.Equal(1200, longest.GetProperty("chars").GetInt32());

        var highlight = res.GetProperty("highlight");
        Assert.Equal(hotSay, highlight.GetProperty("id").GetGuid());
        Assert.Equal(2, highlight.GetProperty("likes").GetInt32());

        // 候选年份含去年；空年份查询回落最新
        var years = res.GetProperty("years").EnumerateArray().Select(y => y.GetInt32()).ToList();
        Assert.Contains(year - 1, years);
        Assert.Equal(years[0], year);
    }

    [Fact]
    public async Task Report_of_user_without_content_returns_empty_flag()
    {
        var user = await _factory.SeedUserAsync("rep3@test.local", "Rep3");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var res = await client.GetFromJsonAsync<JsonElement>("/me/report");
        Assert.True(res.GetProperty("empty").GetBoolean());
        Assert.Equal(0, res.GetProperty("years").GetArrayLength());
    }

    private static DateOnly CnToday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date);
    }

    /// <summary>中国区时刻 → UTC（东八直接减 8 小时；种子固定时刻避免边界漂移）。</summary>
    private static DateTime UtcAt(DateOnly day, double cnHour)
    {
        var cn = day.ToDateTime(TimeOnly.MinValue).AddHours(cnHour);
        return new DateTime(cn.Subtract(TimeSpan.FromHours(8)).Ticks, DateTimeKind.Utc);
    }

    private async Task<Guid> SeedSayAsync(
        Guid userId, DateOnly day, double cnHour, string? city, string? condition, string? tag)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = id,
            AuthorId = userId,
            Text = $"say {day}",
            CreatedAt = UtcAt(day, cnHour),
            UpdatedAt = DateTime.UtcNow,
            WeatherCity = city,
            WeatherCondition = condition,
        });
        if (tag is not null)
        {
            db.ContentTags.Add(new ContentTag { Id = Guid.NewGuid(), ContentId = id, Tag = tag });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedJournalAsync(Guid userId, DateOnly day, string body, string title)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Journal
        {
            Id = id,
            AuthorId = userId,
            Title = title,
            MarkdownBody = body,
            CreatedAt = UtcAt(day, 12),
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }
}
