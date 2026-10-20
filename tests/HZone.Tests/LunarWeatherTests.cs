using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

public class LunarWeatherTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public LunarWeatherTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_with_weather_persists_snapshot_and_lunar()
    {
        var author = await _factory.SeedUserAsync("lw-a@test.local", "LwA");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "晴日随记",
            recipientIds = Array.Empty<Guid>(),
            weather = new { city = "杭州", condition = "晴", tempC = 12.5 },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await ReadJson(create);
        var weather = body.GetProperty("weather");
        Assert.Equal("杭州", weather.GetProperty("city").GetString()!);
        Assert.Equal("晴", weather.GetProperty("condition").GetString()!);
        Assert.Equal(12.5, weather.GetProperty("tempC").GetDouble());
        var lunar = body.GetProperty("lunar");
        Assert.NotEmpty(lunar.GetProperty("ganzhiYear").GetString()!);
        Assert.NotEmpty(lunar.GetProperty("monthDay").GetString()!);
        Assert.NotEmpty(lunar.GetProperty("term").GetString()!);

        var list = await client.GetAsync("/me/contents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = (await ReadJson(list)).GetProperty("items")[0];
        Assert.Equal("杭州", item.GetProperty("weather").GetProperty("city").GetString()!);
        Assert.NotEmpty(item.GetProperty("lunar").GetProperty("monthDay").GetString()!);
    }

    [Fact]
    public async Task Publish_without_weather_keeps_lunar_weather_null()
    {
        var author = await _factory.SeedUserAsync("lw-b@test.local", "LwB");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var create = await client.PostAsJsonAsync("/contents/journals", new
        {
            title = "无天气日志",
            markdown = "body",
            recipientIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await ReadJson(create);
        Assert.True(body.TryGetProperty("weather", out var w) == false || w.ValueKind == JsonValueKind.Null);
        Assert.NotEmpty(body.GetProperty("lunar").GetProperty("monthDay").GetString()!);
    }

    [Fact]
    public async Task Edit_replaces_weather_snapshot()
    {
        var author = await _factory.SeedUserAsync("lw-c@test.local", "LwC");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "later",
            recipientIds = Array.Empty<Guid>(),
        });
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();
        var edit = await client.PutAsJsonAsync($"/contents/{contentId}", new
        {
            weather = new { city = "上海", condition = "小雨", tempC = 18.0 },
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var after = await ReadJson(edit);
        Assert.Equal("上海", after.GetProperty("weather").GetProperty("city").GetString()!);
        Assert.Equal(18.0, after.GetProperty("weather").GetProperty("tempC").GetDouble());

        var old = await SeedOldContentAsync();
        var get = await client.GetAsync($"/contents/{old.id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var oldBody = await ReadJson(get);
        Assert.True(oldBody.TryGetProperty("weather", out var ow) == false || ow.ValueKind == JsonValueKind.Null);
        Assert.NotEmpty(oldBody.GetProperty("lunar").GetProperty("monthDay").GetString()!);
    }

    private async Task<(Guid id, Guid authorId)> SeedOldContentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        var author = db.Users.First(u => u.Email == "lw-c@test.local");
        var say = new Say
        {
            Id = Guid.NewGuid(),
            AuthorId = author.Id,
            Text = "legacy",
            CreatedAt = new DateTime(2025, 6, 1, 4, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 6, 1, 4, 0, 0, DateTimeKind.Utc),
        };
        db.Contents.Add(say);
        await db.SaveChangesAsync();
        return (say.Id, author.Id);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()).RootElement.Clone();
    }
}
