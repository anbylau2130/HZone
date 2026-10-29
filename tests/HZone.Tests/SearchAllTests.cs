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
/// 全局搜索：跨随记正文/笔记标题+正文/可见内容下的评论；kind/tag/时间过滤；
/// 不可见内容与其评论不命中；LIKE 通配符按字面。
/// </summary>
public class SearchAllTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public SearchAllTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Cross_type_hits_with_filters_and_visibility()
    {
        var me = await _factory.SeedUserAsync("sa1@test.local", "Sa1");
        var friend = await _factory.SeedUserAsync("sa2@test.local", "Sa2");
        var stranger = await _factory.SeedUserAsync("sa3@test.local", "Sa3");
        var client = _factory.CreateAuthorizedClient(me.Id, me.Nickname);

        Guid friendSayId;
        var mySayId = await PostSayAsync(client, "今天的晚霞真美");
        var myJournalId = await PostJournalAsync(client, "晚霞笔记", "正文里也有晚霞");
        friendSayId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            // 好友可见（@ 我）
            db.Contents.Add(new Say
            {
                Id = friendSayId, AuthorId = friend.Id, Text = "朋友拍的晚霞",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.ContentRecipients.Add(new ContentRecipient
            {
                Id = Guid.NewGuid(), ContentId = friendSayId, RecipientUserId = me.Id,
            });
            // 陌生人内容（不可见）
            db.Contents.Add(new Say
            {
                Id = Guid.NewGuid(), AuthorId = stranger.Id, Text = "陌生人的晚霞",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            // 朋友在我随记下的评论（含关键词）
            db.Comments.Add(new Comment
            {
                Id = Guid.NewGuid(), ContentId = mySayId, AuthorId = friend.Id,
                Text = "晚霞确实好看！", CreatedAt = DateTime.UtcNow,
            });
            // 陌生人在不可见内容下的评论（不应命中）
            var strangerSay = new Say
            {
                Id = Guid.NewGuid(), AuthorId = stranger.Id, Text = "x",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            db.Contents.Add(strangerSay);
            db.Comments.Add(new Comment
            {
                Id = Guid.NewGuid(), ContentId = strangerSay.Id, AuthorId = stranger.Id,
                Text = "晚霞", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // 全部：我的随记 + 我的笔记 + 好友随记 + 朋友评论 = 4；陌生人 0
        var all = await client.GetFromJsonAsync<JsonElement>("/me/search-all?q=晚霞");
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        var types = all.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString()).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "comment", "journal", "say", "say" }, types);

        // kind=comment 只剩评论，且带 contentId 供跳转
        var comments = await client.GetFromJsonAsync<JsonElement>("/me/search-all?q=晚霞&kind=comment");
        Assert.Equal(1, comments.GetProperty("total").GetInt32());
        Assert.Equal(mySayId, comments.GetProperty("items")[0].GetProperty("contentId").GetGuid());
        Assert.Equal("晚霞确实好看！", comments.GetProperty("items")[0].GetProperty("snippet").GetString());

        // LIKE 通配符按字面：% 查不到多余
        var pct = await client.GetFromJsonAsync<JsonElement>("/me/search-all?q=%");
        Assert.Equal(0, pct.GetProperty("total").GetInt32());
    }

    private static async Task<Guid> PostSayAsync(HttpClient client, string text)
    {
        var res = await client.PostAsJsonAsync("/contents/says",
            new { text, recipientIds = Array.Empty<Guid>() });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> PostJournalAsync(HttpClient client, string title, string body)
    {
        var res = await client.PostAsJsonAsync("/contents/journals",
            new { title, markdown = body, recipientIds = Array.Empty<Guid>() });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
