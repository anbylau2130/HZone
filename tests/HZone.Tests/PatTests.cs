using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// ticket 09：PAT 生成/列表/吊销 + PAT 认证与 session 用户同口径。
/// </summary>
public class PatTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public PatTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Generate_pat_returns_plaintext_once_and_authenticates_owner()
    {
        var user = await _factory.SeedUserAsync("pat8a@test.local", "Pat8a");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var created = await CreatePatAsync(client, "agent token");
        var token = created.GetProperty("token").GetString()!;
        Assert.StartsWith("hz_", token);
        Assert.Equal(25, token.Length);

        // PAT 身份与本人 session 同口径：/auth/me 返回本人信息。
        var me = await PatClient(token).GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
        Assert.Equal("Pat8a", me.GetProperty("nickname").GetString());

        // 列表只含元数据，绝不含 token/tokenHash。
        var list = await client.GetFromJsonAsync<JsonElement>("/pats");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(created.GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
        Assert.False(item.TryGetProperty("token", out _));
        Assert.False(item.TryGetProperty("tokenHash", out _));

        // 名称为空 → 校验错误。
        var empty = await client.PostAsJsonAsync("/pats", new { name = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Pat_posts_say_mention_friend_both_readers()
    {
        var author = await _factory.SeedUserAsync("pat8b@test.local", "Pat8b");
        var friend = await _factory.SeedUserAsync("pat8c@test.local", "Pat8c");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var patToken = (await CreatePatAsync(authorClient, "say bot")).GetProperty("token").GetString()!;

        // PAT 发说说 @ 好友 → 201。
        var patClient = PatClient(patToken);
        var created = await patClient.PostAsJsonAsync("/contents/says", new
        {
            text = "hello from pat",
            recipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var say = await ReadJson(created);
        var sayId = say.GetProperty("id").GetGuid();

        // 好友（JWT）收到 @。
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var received = await friendClient.GetFromJsonAsync<JsonElement>("/me/received");
        Assert.Equal(1, received.GetProperty("total").GetInt32());
        Assert.Equal("hello from pat", received.GetProperty("items")[0].GetProperty("text").GetString());

        // 好友（自己的 PAT）同样可见。
        var friendPatToken = (await CreatePatAsync(friendClient, "reader bot")).GetProperty("token").GetString()!;
        var detail = await PatClient(friendPatToken).GetFromJsonAsync<JsonElement>($"/contents/{sayId}");
        Assert.Equal(sayId, detail.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Pat_reads_own_contents_received_and_search()
    {
        var a = await _factory.SeedUserAsync("pat8d@test.local", "Pat8d");
        var b = await _factory.SeedUserAsync("pat8e@test.local", "Pat8e");
        await _factory.SeedFriendshipAsync(a, b);

        var patToken = (await CreatePatAsync(
            _factory.CreateAuthorizedClient(a.Id, a.Nickname), "diary bot")).GetProperty("token").GetString()!;
        var patClient = PatClient(patToken);

        var journal = await patClient.PostAsJsonAsync("/contents/journals", new
        {
            title = "Pat diary",
            markdown = "pat search mark",
            recipientIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, journal.StatusCode);

        // b（JWT）@a，a 的 PAT 能读到 received。
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);
        await bClient.PostAsJsonAsync("/contents/says", new
        {
            text = "ping a",
            recipientIds = new[] { a.Id },
        });

        var mine = await patClient.GetFromJsonAsync<JsonElement>("/me/contents");
        Assert.Equal(1, mine.GetProperty("total").GetInt32());
        Assert.Equal("journal", mine.GetProperty("items")[0].GetProperty("kind").GetString());

        var received = await patClient.GetFromJsonAsync<JsonElement>("/me/received");
        Assert.Equal(1, received.GetProperty("total").GetInt32());
        Assert.Equal("ping a", received.GetProperty("items")[0].GetProperty("text").GetString());

        var search = await patClient.GetFromJsonAsync<JsonElement>("/search?q=pat%20search");
        Assert.Equal(1, search.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Revoked_pat_gets_401_and_revoke_is_idempotent()
    {
        var user = await _factory.SeedUserAsync("pat8f@test.local", "Pat8f");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var created = await CreatePatAsync(client, "to revoke");
        var patId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("token").GetString()!;

        var revoked = await client.DeleteAsync($"/pats/{patId}");
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        // 已吊销 PAT → 401。
        var after = await PatClient(token).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // 重复吊销幂等 200；列表带 revokedAt。
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/pats/{patId}")).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/pats");
        Assert.True(list.GetProperty("items")[0].TryGetProperty("revokedAt", out _));
    }

    [Fact]
    public async Task Non_owner_or_missing_revoke_returns_404()
    {
        var alice = await _factory.SeedUserAsync("pat8g@test.local", "Pat8g");
        var bob = await _factory.SeedUserAsync("pat8h@test.local", "Pat8h");
        var created = await CreatePatAsync(
            _factory.CreateAuthorizedClient(alice.Id, alice.Nickname), "alice token");
        var patId = created.GetProperty("id").GetGuid();

        // 非本人 404；不存在 404。
        var bobClient = _factory.CreateAuthorizedClient(bob.Id, bob.Nickname);
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.DeleteAsync($"/pats/{patId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bobClient.DeleteAsync($"/pats/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Pat_limit_20_then_409_revoke_frees_slot()
    {
        var user = await _factory.SeedUserAsync("pat8i@test.local", "Pat8i");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        for (var i = 0; i < 20; i++)
        {
            await CreatePatAsync(client, $"pat {i}");
        }

        var overflow = await client.PostAsJsonAsync("/pats", new { name = "21st" });
        Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);

        // 吊销一个后可再建（上限只计活跃 PAT）。
        var list = await client.GetFromJsonAsync<JsonElement>("/pats");
        var items = list.GetProperty("items");
        var oldest = items[items.GetArrayLength() - 1].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/pats/{oldest}")).StatusCode);
        await CreatePatAsync(client, "back under cap");
    }

    [Fact]
    public async Task Outsider_pat_cannot_read_non_visible_content()
    {
        var author = await _factory.SeedUserAsync("pat8j@test.local", "Pat8j");
        var outsider = await _factory.SeedUserAsync("pat8k@test.local", "Pat8k");

        var patToken = (await CreatePatAsync(
            _factory.CreateAuthorizedClient(author.Id, author.Nickname), "author bot")).GetProperty("token").GetString()!;
        var outsiderToken = (await CreatePatAsync(
            _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname), "outsider bot")).GetProperty("token").GetString()!;

        // 作者 PAT 发说说（无 @，仅自己可见）。
        var created = await PatClient(patToken).PostAsJsonAsync("/contents/says", new
        {
            text = "private note",
            recipientIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sayId = (await ReadJson(created)).GetProperty("id").GetGuid();

        // 陌生人 PAT 读非可见内容 → 404（与 ticket 02 口径一致）。
        Assert.Equal(HttpStatusCode.NotFound,
            (await PatClient(outsiderToken).GetAsync($"/contents/{sayId}")).StatusCode);
    }

    private static async Task<JsonElement> CreatePatAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/pats", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJson(response);
    }

    private HttpClient PatClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}

/// <summary>
/// 独立 factory（未初始化实例）：/pats 端点的初始化与匿名防护。
/// </summary>
public class PatAccessTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public PatAccessTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Pat_endpoints_require_initialization_and_authentication()
    {
        var user = await _factory.SeedUserAsync("pataccess8@test.local", "PatAccess8");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/pats", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/pats")).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/pats")).StatusCode);
    }
}

