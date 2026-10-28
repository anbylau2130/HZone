using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 首页时间线键集游标（since 窗口 + before/beforeId 翻页）：
/// 核心不变量——同 CreatedAt（批量连发/导入）翻页不丢条目。
/// </summary>
public class TimelineCursorTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public TimelineCursorTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Cursor_paging_never_loses_items_sharing_the_same_timestamp()
    {
        var user = await _factory.SeedUserAsync("cursor1@test.local", "Cursor1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 三条随记，随后强制同一 CreatedAt（模拟同毫秒连发）
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var res = await client.PostAsJsonAsync("/contents/says",
                new { text = $"tie-{i}", recipientIds = Array.Empty<Guid>() });
            res.EnsureSuccessStatusCode();
            ids.Add((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        var sameAt = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var rows = await db.Contents.Where(c => ids.Contains(c.Id)).ToListAsync();
            foreach (var row in rows) row.CreatedAt = sameAt;
            await db.SaveChangesAsync();
        }

        // 逐条翻页（limit 1）直到取空，收集到的 id 必须与全集一致
        var seen = new List<Guid>();
        string? before = null;
        string? beforeId = null;
        while (true)
        {
            var url = $"/me/contents?limit=1&kind=say";
            if (before is not null) url += $"&before={Uri.EscapeDataString(before)}&beforeId={beforeId}";
            var page = await client.GetFromJsonAsync<JsonElement>(url);
            var arr = page.GetProperty("items").EnumerateArray().ToList();
            if (arr.Count == 0) break;
            var item = arr[0];
            seen.Add(item.GetProperty("id").GetGuid());
            before = item.GetProperty("createdAt").GetString();
            beforeId = item.GetProperty("id").GetString();
            Assert.True(seen.Count <= 5, "cursor paging should terminate");
        }

        Assert.Equal(3, seen.Count);
        Assert.All(ids, id => Assert.Contains(id, seen));
    }

    [Fact]
    public async Task Since_window_returns_only_recent_items()
    {
        var user = await _factory.SeedUserAsync("cursor2@test.local", "Cursor2");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var freshId = (await PostSayAsync(client, "fresh")).GetProperty("id").GetGuid();
        var oldId = (await PostSayAsync(client, "old")).GetProperty("id").GetGuid();
        // 旧条目拨回 30 天前
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var row = await db.Contents.FirstAsync(c => c.Id == oldId);
            row.CreatedAt = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        // 窗口：最近 7 天 → 只见 fresh
        var since = DateTime.UtcNow.AddDays(-7).ToString("O");
        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/me/contents?since={Uri.EscapeDataString(since)}&limit=50");
        var windowIds = page.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(freshId, windowIds);
        Assert.DoesNotContain(oldId, windowIds);

        // 无窗口 → 两台都在（老口径不受影响）
        var all = await client.GetFromJsonAsync<JsonElement>("/me/contents?limit=50");
        var allIds = all.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(freshId, allIds);
        Assert.Contains(oldId, allIds);
    }

    private static async Task<JsonElement> PostSayAsync(HttpClient client, string text)
    {
        var res = await client.PostAsJsonAsync("/contents/says",
            new { text, recipientIds = Array.Empty<Guid>() });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }
}
