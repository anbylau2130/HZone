using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

public class CommentsInboxTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public CommentsInboxTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_with_recipient_writes_mentioned_inbox()
    {
        var author = await _factory.SeedUserAsync("ci-author1@test.local", "CiAuthor1");
        var friend = await _factory.SeedUserAsync("ci-friend1@test.local", "CiFriend1");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "hello friend", recipientIds = new[] { friend.Id } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var count = await ReadJson(await friendClient.GetAsync("/inbox/unread-count"));
        Assert.Equal(1, count.GetProperty("unreadCount").GetInt32());

        var inbox = await ReadJson(await friendClient.GetAsync("/inbox"));
        Assert.Equal(1, inbox.GetProperty("total").GetInt32());
        Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());
        var item = inbox.GetProperty("items")[0];
        Assert.Equal("mentioned", item.GetProperty("type").GetString());
        Assert.Equal(contentId, item.GetProperty("contentId").GetGuid());
        Assert.Equal("say", item.GetProperty("contentKind").GetString());
        Assert.Contains("CiAuthor1", item.GetProperty("previewText").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
    }

    [Fact]
    public async Task Friend_comment_notifies_author_and_appears_in_list()
    {
        var author = await _factory.SeedUserAsync("ci-author2@test.local", "CiAuthor2");
        var friend = await _factory.SeedUserAsync("ci-friend2@test.local", "CiFriend2");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "say to comment on", recipientIds = new[] { friend.Id } });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var comment = await friendClient.PostAsJsonAsync($"/contents/{contentId}/comments",
            new { text = "nice one" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        var commentBody = await ReadJson(comment);
        Assert.Equal(contentId, commentBody.GetProperty("contentId").GetGuid());
        Assert.Equal("nice one", commentBody.GetProperty("text").GetString());
        Assert.Equal(friend.Id, commentBody.GetProperty("author").GetProperty("id").GetGuid());

        var authorInbox = await ReadJson(await authorClient.GetAsync("/inbox"));
        Assert.Equal(1, authorInbox.GetProperty("unreadCount").GetInt32());
        var item = authorInbox.GetProperty("items")[0];
        Assert.Equal("commented", item.GetProperty("type").GetString());
        Assert.Equal(contentId, item.GetProperty("contentId").GetGuid());
        Assert.Contains("CiAuthor2", item.GetProperty("previewText").GetString());

        var list = await ReadJson(await friendClient.GetAsync($"/contents/{contentId}/comments"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("nice one", list.GetProperty("items")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Self_comment_produces_no_inbox()
    {
        var author = await _factory.SeedUserAsync("ci-author3@test.local", "CiAuthor3");

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "private note", recipientIds = Array.Empty<Guid>() });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var comment = await authorClient.PostAsJsonAsync($"/contents/{contentId}/comments",
            new { text = "note to self" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        var inbox = await ReadJson(await authorClient.GetAsync("/inbox"));
        Assert.Equal(0, inbox.GetProperty("total").GetInt32());
        Assert.Equal(0, inbox.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Outsider_gets_404_for_comments()
    {
        var author = await _factory.SeedUserAsync("ci-author4@test.local", "CiAuthor4");
        var friend = await _factory.SeedUserAsync("ci-friend4@test.local", "CiFriend4");
        var outsider = await _factory.SeedUserAsync("ci-outsider4@test.local", "CiOutsider4");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "friend only", recipientIds = new[] { friend.Id } });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var outsiderClient = _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname);
        var post = await outsiderClient.PostAsJsonAsync($"/contents/{contentId}/comments",
            new { text = "sneak comment" });
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        var get = await outsiderClient.GetAsync($"/contents/{contentId}/comments");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Comment_list_is_ascending_with_total()
    {
        var author = await _factory.SeedUserAsync("ci-author5@test.local", "CiAuthor5");
        var friend = await _factory.SeedUserAsync("ci-friend5@test.local", "CiFriend5");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "say with thread", recipientIds = new[] { friend.Id } });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        await friendClient.PostAsJsonAsync($"/contents/{contentId}/comments", new { text = "first" });
        await authorClient.PostAsJsonAsync($"/contents/{contentId}/comments", new { text = "second" });
        await friendClient.PostAsJsonAsync($"/contents/{contentId}/comments", new { text = "third" });

        var list = await ReadJson(await authorClient.GetAsync($"/contents/{contentId}/comments"));
        Assert.Equal(3, list.GetProperty("total").GetInt32());
        var items = list.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("first", items[0].GetProperty("text").GetString());
        Assert.Equal("second", items[1].GetProperty("text").GetString());
        Assert.Equal("third", items[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Mark_read_is_idempotent_and_nonowner_gets_404()
    {
        var author = await _factory.SeedUserAsync("ci-author6@test.local", "CiAuthor6");
        var friend = await _factory.SeedUserAsync("ci-friend6@test.local", "CiFriend6");
        var stranger = await _factory.SeedUserAsync("ci-stranger6@test.local", "CiStranger6");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "to be read", recipientIds = new[] { friend.Id } });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var inbox = await ReadJson(await friendClient.GetAsync("/inbox"));
        var messageId = inbox.GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await friendClient.PostAsync($"/inbox/{messageId}/read", null)).StatusCode);
        var after = await ReadJson(await friendClient.GetAsync("/inbox/unread-count"));
        Assert.Equal(0, after.GetProperty("unreadCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK,
            (await friendClient.PostAsync($"/inbox/{messageId}/read", null)).StatusCode);
        var afterAgain = await ReadJson(await friendClient.GetAsync("/inbox/unread-count"));
        Assert.Equal(0, afterAgain.GetProperty("unreadCount").GetInt32());

        var strangerClient = _factory.CreateAuthorizedClient(stranger.Id, stranger.Nickname);
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.PostAsync($"/inbox/{messageId}/read", null)).StatusCode);
    }

    [Fact]
    public async Task Content_delete_cascades_inbox_messages()
    {
        var author = await _factory.SeedUserAsync("ci-author7@test.local", "CiAuthor7");
        var friend = await _factory.SeedUserAsync("ci-friend7@test.local", "CiFriend7");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "doomed say", recipientIds = new[] { friend.Id } });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var before = await ReadJson(await friendClient.GetAsync("/inbox/unread-count"));
        Assert.Equal(1, before.GetProperty("unreadCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent,
            (await authorClient.DeleteAsync($"/contents/{contentId}")).StatusCode);

        var after = await ReadJson(await friendClient.GetAsync("/inbox/unread-count"));
        Assert.Equal(0, after.GetProperty("unreadCount").GetInt32());
        var inbox = await ReadJson(await friendClient.GetAsync("/inbox"));
        Assert.Equal(0, inbox.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Comment_text_validation()
    {
        var author = await _factory.SeedUserAsync("ci-author8@test.local", "CiAuthor8");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var create = await client.PostAsJsonAsync("/contents/says",
            new { text = "own say", recipientIds = Array.Empty<Guid>() });
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/contents/{contentId}/comments", new { text = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/contents/{contentId}/comments", new { text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/contents/{contentId}/comments",
                new { text = new string('x', 2001) })).StatusCode);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
