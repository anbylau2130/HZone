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
/// 心情追踪：随记建/改携带 moodLevel（校验 1~5，0=清除）；/me/moods 按中国区
/// 自然日聚合日均值、按天气分组均值与交叉洞察（≥2 种天气、各 ≥2 条才给洞察）。
/// </summary>
public class MoodTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public MoodTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Say_mood_roundtrip_and_update_semantics()
    {
        var user = await _factory.SeedUserAsync("mood1@test.local", "Mood1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var created = await client.PostAsJsonAsync("/contents/says",
            new { text = "今天很开心", recipientIds = Array.Empty<Guid>(), moodLevel = 5 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal(5, body.GetProperty("moodLevel").GetInt32());

        // 列表带出 moodLevel；笔记恒 null
        var list = await client.GetFromJsonAsync<JsonElement>("/me/contents?limit=5");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(5, item.GetProperty("moodLevel").GetInt32());

        // 改心情 3 → 0 清除
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/contents/{id}", new { moodLevel = 3 })).StatusCode);
        var list2 = await client.GetFromJsonAsync<JsonElement>("/me/contents?limit=5");
        Assert.Equal(3, Assert.Single(list2.GetProperty("items").EnumerateArray())
            .GetProperty("moodLevel").GetInt32());
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/contents/{id}", new { moodLevel = 0 })).StatusCode);
        var list3 = await client.GetFromJsonAsync<JsonElement>("/me/contents?limit=5");
        Assert.Null(Assert.Single(list3.GetProperty("items").EnumerateArray())
            .GetProperty("moodLevel").GetString());

        // 非法档位 → 400（创建 6 / 更新 -1）
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/contents/says",
            new { text = "x", recipientIds = Array.Empty<Guid>(), moodLevel = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/contents/{id}", new { moodLevel = -1 })).StatusCode);
    }

    [Fact]
    public async Task Moods_aggregate_daily_avg_weather_groups_and_insight()
    {
        var user = await _factory.SeedUserAsync("mood2@test.local", "Mood2");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 两天：昨天 2 条晴天（5、3 → 日均 4），今天 2 条雨天（2、2 → 日均 2）
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date);
        await SeedMoodSayAsync(user.Id, today.AddDays(-1), 12, 5, "sunny");
        await SeedMoodSayAsync(user.Id, today.AddDays(-1), 13, 3, "sunny");
        await SeedMoodSayAsync(user.Id, today, 12, 2, "rain");
        await SeedMoodSayAsync(user.Id, today, 13, 2, "rain");
        await SeedMoodSayAsync(user.Id, today, 14, null, "cloudy"); // 无心情：不参与聚合

        var res = await client.GetFromJsonAsync<JsonElement>("/me/moods?days=7");
        Assert.Equal(4, res.GetProperty("totalMarked").GetInt32());

        var byDay = res.GetProperty("byDay").EnumerateArray().ToList();
        Assert.Equal(2, byDay.Count);
        Assert.Equal(today.AddDays(-1).ToString("yyyy-MM-dd"), byDay[0].GetProperty("date").GetString());
        Assert.Equal(4, byDay[0].GetProperty("avg").GetDouble());
        Assert.Equal("sunny", byDay[0].GetProperty("condition").GetString());
        Assert.Equal(2, byDay[1].GetProperty("avg").GetDouble());

        var byWeather = res.GetProperty("byWeather").EnumerateArray().ToList();
        Assert.Equal(2, byWeather.Count);
        Assert.Equal("sunny", byWeather[0].GetProperty("condition").GetString());
        Assert.Equal(4, byWeather[0].GetProperty("avg").GetDouble());

        var insight = res.GetProperty("insight");
        Assert.Equal("sunny", insight.GetProperty("best").GetString());
        Assert.Equal("rain", insight.GetProperty("worst").GetString());
        Assert.Equal(2, insight.GetProperty("gap").GetDouble());
    }

    private async Task SeedMoodSayAsync(
        Guid userId, DateOnly day, double cnHour, int? mood, string? condition)
    {
        var cn = day.ToDateTime(TimeOnly.MinValue).AddHours(cnHour);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Contents.Add(new Say
        {
            Id = Guid.NewGuid(),
            AuthorId = userId,
            Text = "mood",
            MoodLevel = mood,
            WeatherCondition = condition,
            CreatedAt = new DateTime(cn.Subtract(TimeSpan.FromHours(8)).Ticks, DateTimeKind.Utc),
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
