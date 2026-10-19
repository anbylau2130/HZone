using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

public class VisibilityTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public VisibilityTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Private_say_visible_only_to_author()
    {
        var author = await _factory.SeedUserAsync("author1@test.local", "Author1");
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "private note", recipientIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        var contentId = created.GetProperty("id").GetString();

        var authorGet = await authorClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.OK, authorGet.StatusCode);

        var anonymous = _factory.CreateClient();
        var anonymousGet = await anonymous.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);
    }

    [Fact]
    public async Task Mentioned_friend_can_read_non_friend_gets_404()
    {
        var author = await _factory.SeedUserAsync("author2@test.local", "Author2");
        var friend = await _factory.SeedUserAsync("friend2@test.local", "Friend2");
        var outsider = await _factory.SeedUserAsync("outsider2@test.local", "Outsider2");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "hello friend", recipientIds = new[] { friend.Id } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        var contentId = created.GetProperty("id").GetString();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var friendGet = await friendClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.OK, friendGet.StatusCode);

        var outsiderClient = _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname);
        var outsiderGet = await outsiderClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.NotFound, outsiderGet.StatusCode);

        var anonymousGet = await _factory.CreateClient().GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);
    }

    [Fact]
    public async Task Mentioning_non_friend_returns_400()
    {
        var author = await _factory.SeedUserAsync("author3@test.local", "Author3");
        var outsider = await _factory.SeedUserAsync("outsider3@test.local", "Outsider3");

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "should fail", recipientIds = new[] { outsider.Id } });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Journal_visible_to_mentioned_friend_with_full_body()
    {
        var author = await _factory.SeedUserAsync("author4@test.local", "Author4");
        var friend = await _factory.SeedUserAsync("friend4@test.local", "Friend4");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/journals",
            new { title = "My Journal", markdown = "# hello", recipientIds = new[] { friend.Id } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        var contentId = created.GetProperty("id").GetString();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var friendGet = await friendClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.OK, friendGet.StatusCode);

        var body = await ReadJson(friendGet);
        Assert.Equal("journal", body.GetProperty("kind").GetString());
        Assert.Equal("My Journal", body.GetProperty("title").GetString());
        Assert.Equal("# hello", body.GetProperty("markdownBody").GetString());
        Assert.Equal(author.Id, body.GetProperty("authorId").GetGuid());

        var recipients = body.GetProperty("recipients");
        Assert.Equal(1, recipients.GetArrayLength());
        Assert.Equal(friend.Id, recipients[0].GetProperty("userId").GetGuid());
        Assert.Equal("Friend4", recipients[0].GetProperty("nickname").GetString());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
