using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// ticket 15 验收补全：spec 用户故事中的负路径与跨故事旅程。
/// 已覆盖故事的常规路径见各自 ticket 的测试类（AuthFlowTests / VisibilityTests / PatTests 等）。
/// </summary>
public class UserStoryCoverageTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public UserStoryCoverageTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // story 13：好友关系本身不授予内容可见权——好友但未被 @ 访问 → 404（存在性不泄露）。
    [Fact]
    public async Task Friend_but_not_mentioned_gets_404()
    {
        var author = await _factory.SeedUserAsync("uc-author@test.local", "UcAuthor");
        var friend = await _factory.SeedUserAsync("uc-friend@test.local", "UcFriend");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "not for you", recipientIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        Assert.Equal(HttpStatusCode.NotFound, (await friendClient.GetAsync($"/contents/{contentId}")).StatusCode);

        // 好友也看不到作者主页上的这条私密内容（列表不泄露）
        var authorContents = await friendClient.GetAsync($"/users/{author.Id}/contents");
        authorContents.EnsureSuccessStatusCode();
        var page = await ReadJson(authorContents);
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }

    // story 14：作者修改可见名单——新增者获得访问权并收 Mentioned 站内信；被移除者立即失权。
    [Fact]
    public async Task Edit_recipients_new_gains_access_removed_loses_immediately()
    {
        var author = await _factory.SeedUserAsync("uc-author14@test.local", "UcAuthor14");
        var bob = await _factory.SeedUserAsync("uc-bob14@test.local", "UcBob14");
        var carol = await _factory.SeedUserAsync("uc-carol14@test.local", "UcCarol14");
        await _factory.SeedFriendshipAsync(author, bob);
        await _factory.SeedFriendshipAsync(author, carol);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "audience change", recipientIds = new[] { bob.Id } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();

        // 初始：Bob 可见，Carol 404
        var bobClient = _factory.CreateAuthorizedClient(bob.Id, bob.Nickname);
        var carolClient = _factory.CreateAuthorizedClient(carol.Id, carol.Nickname);
        Assert.Equal(HttpStatusCode.OK, (await bobClient.GetAsync($"/contents/{contentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carolClient.GetAsync($"/contents/{contentId}")).StatusCode);

        // 编辑名单：[Bob] → [Carol]
        var update = await authorClient.PutAsJsonAsync($"/contents/{contentId}",
            new { recipientIds = new[] { carol.Id } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJson(update);
        var recipientIds = updated.GetProperty("recipients").EnumerateArray()
            .Select(r => r.GetProperty("userId").GetGuid()).ToList();
        Assert.Equal(new[] { carol.Id }, recipientIds);

        // Bob 立即失权（详情 404 + 收到列表不再出现）
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.GetAsync($"/contents/{contentId}")).StatusCode);
        var bobReceived = await ReadJson(await bobClient.GetAsync("/me/received"));
        Assert.Equal(0, bobReceived.GetProperty("total").GetInt32());

        // Carol 立即可见，且收到 Mentioned 站内信
        Assert.Equal(HttpStatusCode.OK, (await carolClient.GetAsync($"/contents/{contentId}")).StatusCode);
        var carolInbox = await ReadJson(await carolClient.GetAsync("/inbox"));
        var mentioned = carolInbox.GetProperty("items").EnumerateArray()
            .Any(m => m.GetProperty("type").GetString() == "mentioned"
                && m.GetProperty("contentId").GetGuid() == Guid.Parse(contentId!));
        Assert.True(mentioned, "new recipient should receive a mentioned inbox message");

        // Bob 保留自己作为好友的身份（名单编辑不影响好友关系）
        var bobFriends = await ReadJson(await bobClient.GetAsync("/friends"));
        Assert.Equal(1, bobFriends.GetArrayLength());
    }

    // story 14/15：编辑正文后，名单内好友仍可见且看到最新内容。
    [Fact]
    public async Task Edit_body_keeps_remaining_recipient_access_with_new_text()
    {
        var author = await _factory.SeedUserAsync("uc-author15@test.local", "UcAuthor15");
        var friend = await _factory.SeedUserAsync("uc-friend15@test.local", "UcFriend15");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/journals",
            new { title = "Original Title", markdown = "v1", recipientIds = new[] { friend.Id } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();

        var update = await authorClient.PutAsJsonAsync($"/contents/{contentId}",
            new { title = "Edited Title", markdownBody = "v2 edited", recipientIds = new[] { friend.Id } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var friendGet = await friendClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.OK, friendGet.StatusCode);
        var body = await ReadJson(friendGet);
        Assert.Equal("Edited Title", body.GetProperty("title").GetString());
        Assert.Equal("v2 edited", body.GetProperty("markdownBody").GetString());
    }

    // stories 2/3/7/12：邀请码注册新用户的完整 HTTP 旅程——管理员发码 → 注册（混合大小写邮箱，
    // 邮箱即账号）→ .eml 提取 token 验证 → 邮箱+密码登录 → 加好友 → @ 发布 → 被邀者可见 + 站内信。
    [Fact]
    public async Task Invite_journey_register_verify_friend_mention_visible()
    {
        // 管理员（bootstrap 管理员）登录并创建邀请码
        var admin = await _factory.EnsureAdminAsync();
        var adminClient = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var inviteResponse = await adminClient.PostAsync("/invites", content: null);
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        var code = (await ReadJson(inviteResponse)).GetProperty("code").GetString();

        // 邀请码注册（邮箱混合大小写 → 归一化为小写，登录用邮箱）
        var mixedCaseEmail = "Journey.User@Test.Local";
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/auth/register",
            new { inviteCode = code, email = mixedCaseEmail, password = "journey pass 1", nickname = "Journey" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        // .eml 提取验证 token → 验证
        var verifyToken = await EmlHelper.ExtractLatestTokenAsync(_factory.DataDirectory, "verify-email");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/verify-email",
            new { token = verifyToken })).StatusCode);

        // 登录（用邮箱，大小写不敏感）→ JWT
        var login = await client.PostAsJsonAsync("/auth/login",
            new { email = mixedCaseEmail, password = "journey pass 1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var jwt = (await ReadJson(login)).GetProperty("token").GetString()!;

        // 与已存在用户互加好友（申请 → 同意，双向）
        var inviter = await _factory.SeedUserAsync("uc-inviter@test.local", "UcInviter");
        var journeyRequest = new HttpRequestMessage(HttpMethod.Post, "/friends/requests")
        {
            Content = JsonContent.Create(new { targetUserId = inviter.Id }),
        };
        journeyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var friendRequest = await client.SendAsync(journeyRequest);
        Assert.Equal(HttpStatusCode.Created, friendRequest.StatusCode);

        var inviterClient = _factory.CreateAuthorizedClient(inviter.Id, inviter.Nickname);
        var incoming = await ReadJson(await inviterClient.GetAsync("/friends/requests"));
        var requestId = incoming.GetProperty("incoming").EnumerateArray().First()
            .GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await inviterClient.PostAsync($"/friends/requests/{requestId}/accept", null)).StatusCode);

        // 邀请者 @ 新用户发布说说
        var journeyUserId = await GetUserIdByEmail("journey.user@test.local");
        var create = await inviterClient.PostAsJsonAsync("/contents/says",
            new { text = "welcome journey friend", recipientIds = new[] { journeyUserId } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();

        // 新用户可见该说说 + 收到 Mentioned 站内信
        var journeyGet = new HttpRequestMessage(HttpMethod.Get, $"/contents/{contentId}");
        journeyGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(journeyGet)).StatusCode);

        var journeyInbox = new HttpRequestMessage(HttpMethod.Get, "/inbox");
        journeyInbox.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var inbox = await ReadJson(await client.SendAsync(journeyInbox));
        Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Contains(inbox.GetProperty("items").EnumerateArray(),
            m => m.GetProperty("type").GetString() == "mentioned");
    }

    private async Task<Guid> GetUserIdByEmail(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
}
