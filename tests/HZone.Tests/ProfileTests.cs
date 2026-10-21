using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 签名与封面（moments-wechat 票 01）：PUT /me/profile、POST/DELETE /me/cover
/// + 全站 DTO（me/login/friends/author 节点）signature/coverUrl 下发。
/// </summary>
public class ProfileTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public ProfileTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Update_signature_reflects_in_me_friends_and_author_node()
    {
        var author = await _factory.SeedUserAsync("profile-a@test.local", "ProfileA");
        var friend = await _factory.SeedUserAsync("profile-b@test.local", "ProfileB");
        await _factory.SeedFriendshipAsync(author, friend);
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // PUT /me/profile → 200 { signature }。
        var update = await client.PutAsJsonAsync("/me/profile", new { signature = "hello signature" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("hello signature",
            (await ReadJson(update)).GetProperty("signature").GetString());

        // /auth/me 生效。
        var me = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal("hello signature", me.GetProperty("signature").GetString());

        // GET /friends 生效（好友列表带好友自己的签名）。
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        await friendClient.PutAsJsonAsync("/me/profile", new { signature = "friend sig" });
        var friends = await ReadJson(await client.GetAsync("/friends"));
        Assert.Equal("friend sig",
            friends[0].GetProperty("signature").GetString());

        // 发布内容 → author.signature 一致。
        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "with signature",
            recipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();
        var get = await client.GetAsync($"/contents/{contentId}");
        var authorNode = (await ReadJson(get)).GetProperty("author");
        Assert.Equal("hello signature", authorNode.GetProperty("signature").GetString());
        Assert.Equal(JsonValueKind.Null, authorNode.GetProperty("coverUrl").ValueKind);
    }

    [Fact]
    public async Task Signature_over_100_chars_returns_400()
    {
        var author = await _factory.SeedUserAsync("profile-c@test.test", "ProfileC");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var update = await client.PutAsJsonAsync("/me/profile",
            new { signature = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Upload_cover_reflects_in_me_and_author_node()
    {
        var author = await _factory.SeedUserAsync("profile-d@test.local", "ProfileD");
        var friend = await _factory.SeedUserAsync("profile-e@test.local", "ProfileE");
        await _factory.SeedFriendshipAsync(author, friend);
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 上传封面（小 PNG）→ 200 { coverUrl }，路径在 /media/ 下。
        var upload = await PostCoverAsync(client, CreatePng(96, 64));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var coverUrl = (await ReadJson(upload)).GetProperty("coverUrl").GetString()!;
        Assert.StartsWith("/media/", coverUrl);

        // GET /media/{storedName} 可下载。
        var download = await client.GetAsync(coverUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        // /auth/me 与 author 节点一致。
        var me = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal(coverUrl, me.GetProperty("coverUrl").GetString());
        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "with cover",
            recipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();
        var get = await client.GetAsync($"/contents/{contentId}");
        var authorNode = (await ReadJson(get)).GetProperty("author");
        Assert.Equal(coverUrl, authorNode.GetProperty("coverUrl").GetString());

        // 清空封面 → 204；/auth/me coverUrl 为 null。
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/me/cover")).StatusCode);
        var meAfter = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal(JsonValueKind.Null, meAfter.GetProperty("coverUrl").ValueKind);
    }

    [Fact]
    public async Task Non_image_cover_returns_400()
    {
        var author = await _factory.SeedUserAsync("profile-f@test.local", "ProfileF");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 伪造 image Content-Type 的文本字节：解码失败 → 400。
        var response = await PostCoverAsync(client, "not an image"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // 非 image Content-Type：直接 400。
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("hello"u8.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(content, "image", "note.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/me/cover", form)).StatusCode);
    }

    [Fact]
    public async Task Profile_endpoints_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/me/profile", new { signature = "x" })).StatusCode);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "image", "x.png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/me/cover", form)).StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCoverAsync(HttpClient client, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "image", "cover.png");
        return await client.PostAsync("/me/cover", form);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24((byte)(x * 4), (byte)(y * 4), 128);
                }
            }
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
