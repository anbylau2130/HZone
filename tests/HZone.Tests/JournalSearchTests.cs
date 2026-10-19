using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

public class JournalSearchTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public JournalSearchTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Home_timeline_mentioned_friend_sees_journal_outsider_gets_empty()
    {
        var author = await _factory.SeedUserAsync("author7a@test.local", "Author7a");
        var friend = await _factory.SeedUserAsync("friend7a@test.local", "Friend7a");
        var outsider = await _factory.SeedUserAsync("outsider7a@test.local", "Outsider7a");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var created = await CreateJournalAsync(
            authorClient, "Travel notes", "# trip", new[] { friend.Id });
        var contentId = created.GetProperty("id").GetGuid();

        // 名单内好友：作者主页时间线可见该日志。
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var timeline = await friendClient.GetFromJsonAsync<JsonElement>($"/users/{author.Id}/contents");
        Assert.Equal(1, timeline.GetProperty("total").GetInt32());
        Assert.Equal(contentId, timeline.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("journal", timeline.GetProperty("items")[0].GetProperty("kind").GetString());

        // 非好友：空数组 200，不泄露存在性。
        var outsiderClient = _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname);
        var outsiderTimeline = await outsiderClient.GetFromJsonAsync<JsonElement>($"/users/{author.Id}/contents");
        Assert.Equal(0, outsiderTimeline.GetProperty("total").GetInt32());
        Assert.Equal(0, outsiderTimeline.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Me_contents_only_own_with_kind_filter()
    {
        var a = await _factory.SeedUserAsync("author7b@test.local", "Author7b");
        var b = await _factory.SeedUserAsync("author7c@test.local", "Author7c");

        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);
        await CreateSayAsync(aClient, "my own say", Array.Empty<Guid>());
        await CreateJournalAsync(aClient, "my own journal", "body", Array.Empty<Guid>());
        await CreateJournalAsync(bClient, "someone else journal", "body", Array.Empty<Guid>());

        var mine = await aClient.GetFromJsonAsync<JsonElement>("/me/contents");
        Assert.Equal(2, mine.GetProperty("total").GetInt32());
        foreach (var item in mine.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(a.Id, item.GetProperty("author").GetProperty("id").GetGuid());
        }

        var says = await aClient.GetFromJsonAsync<JsonElement>("/me/contents?kind=say");
        Assert.Equal(1, says.GetProperty("total").GetInt32());
        Assert.Equal("say", says.GetProperty("items")[0].GetProperty("kind").GetString());

        var journals = await aClient.GetFromJsonAsync<JsonElement>("/me/contents?kind=journal");
        Assert.Equal(1, journals.GetProperty("total").GetInt32());
        Assert.Equal("my own journal", journals.GetProperty("items")[0].GetProperty("title").GetString());

        var badKind = await aClient.GetAsync("/me/contents?kind=photo");
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
    }

    [Fact]
    public async Task Received_lists_mentions_from_others_excluding_own()
    {
        var a = await _factory.SeedUserAsync("author7d@test.local", "Author7d");
        var b = await _factory.SeedUserAsync("author7e@test.local", "Author7e");
        await _factory.SeedFriendshipAsync(a, b);

        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);
        await CreateSayAsync(aClient, "hello b", new[] { b.Id });
        await CreateSayAsync(bClient, "hello a", new[] { a.Id });
        await CreateSayAsync(aClient, "no mention", Array.Empty<Guid>());

        // b 的 received：只含 a 的 @，不含 b 自己发的内容。
        var bReceived = await bClient.GetFromJsonAsync<JsonElement>("/me/received");
        Assert.Equal(1, bReceived.GetProperty("total").GetInt32());
        Assert.Equal("hello b", bReceived.GetProperty("items")[0].GetProperty("text").GetString());
        Assert.Equal(a.Id, bReceived.GetProperty("items")[0].GetProperty("author").GetProperty("id").GetGuid());

        // a 的 received：只含 b 的 @，a 自己 @ 自己发的不算。
        var aReceived = await aClient.GetFromJsonAsync<JsonElement>("/me/received");
        Assert.Equal(1, aReceived.GetProperty("total").GetInt32());
        Assert.Equal("hello a", aReceived.GetProperty("items")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Search_hits_title_body_and_say_text_own_only()
    {
        var a = await _factory.SeedUserAsync("author7f@test.local", "Author7f");
        var b = await _factory.SeedUserAsync("author7g@test.local", "Author7g");

        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);
        await CreateJournalAsync(aClient, "Zenith plans", "secret content qbodymark", Array.Empty<Guid>());
        await CreateSayAsync(aClient, "zenith quick note", Array.Empty<Guid>());
        // b 的内容同样命中关键词，但 a 搜索不到（仅搜自己的）。
        await CreateJournalAsync(bClient, "Other zenith diary", "qbodymark", Array.Empty<Guid>());

        var byBody = await aClient.GetFromJsonAsync<JsonElement>("/search?q=qbodymark");
        Assert.Equal(1, byBody.GetProperty("total").GetInt32());
        Assert.Equal("Zenith plans", byBody.GetProperty("items")[0].GetProperty("title").GetString());

        var byTitle = await aClient.GetFromJsonAsync<JsonElement>("/search?q=Zenith%20plans");
        Assert.Equal(1, byTitle.GetProperty("total").GetInt32());

        var bySayText = await aClient.GetFromJsonAsync<JsonElement>("/search?q=zenith%20quick");
        Assert.Equal(1, bySayText.GetProperty("total").GetInt32());
        Assert.Equal("say", bySayText.GetProperty("items")[0].GetProperty("kind").GetString());

        var miss = await aClient.GetFromJsonAsync<JsonElement>("/search?q=notfoundkeyword");
        Assert.Equal(0, miss.GetProperty("total").GetInt32());
        Assert.Equal(0, miss.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_pagination_returns_total_offset_limit()
    {
        var a = await _factory.SeedUserAsync("author7h@test.local", "Author7h");
        var client = _factory.CreateAuthorizedClient(a.Id, a.Nickname);

        // 时间倒序分页依赖 CreatedAt 可区分，逐条落库。
        var created = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var body = await CreateJournalAsync(client, $"page journal {i}", "body", Array.Empty<Guid>());
            created.Add(body.GetProperty("id").GetGuid());
            await Task.Delay(10);
        }

        var page1 = await client.GetFromJsonAsync<JsonElement>("/me/contents?limit=2");
        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(0, page1.GetProperty("offset").GetInt32());
        Assert.Equal(2, page1.GetProperty("limit").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());

        var page2 = await client.GetFromJsonAsync<JsonElement>("/me/contents?offset=2&limit=2");
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(created[0], page2.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Detail_returns_unified_shape_with_author_media_comment_count()
    {
        var a = await _factory.SeedUserAsync("author7i@test.local", "Author7i");
        var friend = await _factory.SeedUserAsync("friend7i@test.local", "Friend7i");
        await _factory.SeedFriendshipAsync(a, friend);

        var client = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var created = await CreateSayAsync(client, "detail check", new[] { friend.Id });
        var contentId = created.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.Comments.Add(new Comment
            {
                Id = Guid.NewGuid(),
                ContentId = contentId,
                AuthorId = friend.Id,
                Text = "nice",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<JsonElement>($"/contents/{contentId}");
        Assert.Equal(a.Id, detail.GetProperty("author").GetProperty("id").GetGuid());
        Assert.Equal("Author7i", detail.GetProperty("author").GetProperty("nickname").GetString());
        Assert.Equal(1, detail.GetProperty("commentCount").GetInt32());
        Assert.Equal(1, detail.GetProperty("recipients").GetArrayLength());
        Assert.Equal(friend.Id, detail.GetProperty("recipients")[0].GetProperty("userId").GetGuid());
        Assert.Equal(0, detail.GetProperty("media").GetArrayLength());
        Assert.True(detail.GetProperty("updatedAt").GetDateTime() > DateTime.MinValue);
    }

    private static async Task<JsonElement> CreateJournalAsync(
        HttpClient client, string title, string markdown, IReadOnlyList<Guid> recipientIds)
    {
        var response = await client.PostAsJsonAsync("/contents/journals", new
        {
            title,
            markdown,
            recipientIds,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJson(response);
    }

    private static async Task<JsonElement> CreateSayAsync(
        HttpClient client, string text, IReadOnlyList<Guid> recipientIds)
    {
        var response = await client.PostAsJsonAsync("/contents/says", new
        {
            text,
            recipientIds,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJson(response);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}

/// <summary>
/// 独立 factory（未初始化实例）：列表端点的访问控制。
/// </summary>
public class JournalSearchAccessTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public JournalSearchAccessTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_endpoints_require_initialization_and_authentication()
    {
        var author = await _factory.SeedUserAsync("access7a@test.local", "Access7a");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        foreach (var url in new[]
                 {
                     "/me/contents",
                     "/me/received",
                     $"/users/{author.Id}/contents",
                     "/search?q=x",
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(url)).StatusCode);
        }

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/me/contents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/search?q=x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/users/{author.Id}/contents")).StatusCode);
    }
}
