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
/// 头像配置（composer-polish 票 05）：POST/DELETE /me/avatar + 全站 author 节点 avatarUrl 下发。
/// </summary>
public class AvatarTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public AvatarTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upload_avatar_reflects_in_me_content_and_clears()
    {
        var author = await _factory.SeedUserAsync("avatar-a@test.local", "AvatarA");
        var friend = await _factory.SeedUserAsync("avatar-b@test.local", "AvatarB");
        await _factory.SeedFriendshipAsync(author, friend);
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 上传头像（小 PNG）→ 200 { avatarUrl }，路径在 /media/ 下。
        var upload = await PostAvatarAsync(client, CreatePng(48, 48));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var avatarUrl = (await ReadJson(upload)).GetProperty("avatarUrl").GetString()!;
        Assert.StartsWith("/media/", avatarUrl);

        // GET /media/{storedName} 可下载（登录态）。
        var download = await client.GetAsync(avatarUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        // /auth/me 含相同 avatarUrl。
        var me = await client.GetAsync("/auth/me");
        var meBody = await ReadJson(me);
        Assert.Equal(avatarUrl, meBody.GetProperty("avatarUrl").GetString());

        // 发布内容 → author.avatarUrl 一致。
        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "with avatar",
            recipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();
        var get = await client.GetAsync($"/contents/{contentId}");
        var author0 = (await ReadJson(get)).GetProperty("author");
        Assert.Equal(avatarUrl, author0.GetProperty("avatarUrl").GetString());

        // 清除头像 → 204；/auth/me avatarUrl 为 null。
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/me/avatar")).StatusCode);
        var meAfter = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal(JsonValueKind.Null, meAfter.GetProperty("avatarUrl").ValueKind);
    }

    [Fact]
    public async Task Non_image_upload_returns_400()
    {
        var author = await _factory.SeedUserAsync("avatar-c@test.local", "AvatarC");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 伪造 image Content-Type 的文本字节：解码失败 → 400。
        var response = await PostAvatarAsync(client, "not an image"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // 非 image Content-Type：直接 400。
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("hello"u8.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(content, "image", "note.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/me/avatar", form)).StatusCode);
    }

    [Fact]
    public async Task Avatar_endpoints_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "image", "x.png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/me/avatar", form)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/me/avatar")).StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAvatarAsync(HttpClient client, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "image", "avatar.png");
        return await client.PostAsync("/me/avatar", form);
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
