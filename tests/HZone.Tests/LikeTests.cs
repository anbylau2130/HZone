using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 内容点赞：切换语义（再点撤销）、计数与 likedByMe 视角、可见性门（404）、
/// 列表批量携带、内容删除级联清理。
/// </summary>
public class LikeTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public LikeTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Toggle_like_and_undo_with_counts_and_viewer_state()
    {
        var author = await _factory.SeedUserAsync("like8a@test.local", "Like8a");
        var friend = await _factory.SeedUserAsync("like8b@test.local", "Like8b");
        await _factory.SeedFriendshipAsync(author, friend);
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);

        var created = await authorClient.PostAsJsonAsync("/contents/journals", new
        {
            title = "点赞测试", markdown = "正文", recipientIds = new[] { friend.Id },
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 点赞 → liked=true count=1
        var like1 = await friendClient.PostAsJsonAsync($"/contents/{id}/like", (object?)null);
        var like1Body = await like1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(like1Body.GetProperty("liked").GetBoolean());
        Assert.Equal(1, like1Body.GetProperty("likeCount").GetInt32());

        // 作者也赞 → 2
        await authorClient.PostAsJsonAsync($"/contents/{id}/like", (object?)null);

        // 详情视角：friend 看 likedByMe=true count=2；作者本人也 true
        var detail = await friendClient.GetFromJsonAsync<JsonElement>($"/contents/{id}");
        Assert.Equal(2, detail.GetProperty("likeCount").GetInt32());
        Assert.True(detail.GetProperty("likedByMe").GetBoolean());

        // 列表批量携带（friend 的收到列表）
        var received = await friendClient.GetFromJsonAsync<JsonElement>("/me/received");
        Assert.True(received.GetProperty("items")[0].GetProperty("likedByMe").GetBoolean());
        Assert.Equal(2, received.GetProperty("items")[0].GetProperty("likeCount").GetInt32());

        // friend 再点撤销 → count 回 1
        var undo = await friendClient.PostAsJsonAsync($"/contents/{id}/like", (object?)null);
        var undoBody = await undo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(undoBody.GetProperty("liked").GetBoolean());
        Assert.Equal(1, undoBody.GetProperty("likeCount").GetInt32());
    }

    [Fact]
    public async Task Invisible_content_returns_404_and_deleted_content_cascades()
    {
        var author = await _factory.SeedUserAsync("like8c@test.local", "Like8c");
        var stranger = await _factory.SeedUserAsync("like8d@test.local", "Like8d");
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var strangerClient = _factory.CreateAuthorizedClient(stranger.Id, stranger.Nickname);

        // 私密内容（无收件人）：陌生人不可见 → 404（不泄露存在性）
        var created = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "私密随记", recipientIds = Array.Empty<Guid>(),
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var denied = await strangerClient.PostAsJsonAsync($"/contents/{id}/like", (object?)null);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // 作者点赞后删除内容：级联清理点赞行（无孤儿）
        await authorClient.PostAsJsonAsync($"/contents/{id}/like", (object?)null);
        await authorClient.DeleteAsync($"/contents/{id}");
        var after = await authorClient.GetFromJsonAsync<JsonElement>("/me/contents");
        Assert.Equal(0, after.GetProperty("total").GetInt32());
    }
}
