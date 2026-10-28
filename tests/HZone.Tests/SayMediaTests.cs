using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using HZone.Tests.Helpers;

namespace HZone.Tests;

public class SayMediaTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public SayMediaTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_say_with_two_images_friend_downloads_media_outsider_404()
    {
        var author = await _factory.SeedUserAsync("author6a@test.local", "Author6a");
        var friend = await _factory.SeedUserAsync("friend6a@test.local", "Friend6a");
        var outsider = await _factory.SeedUserAsync("outsider6a@test.local", "Outsider6a");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var m1 = await UploadImageAsync(authorClient, CreatePng(64, 48));
        var m2 = await UploadImageAsync(authorClient, CreatePng(48, 64));

        var create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "with photos",
            recipientIds = new[] { friend.Id },
            mediaIds = new[] { m1.Id, m2.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // 名单内好友：GET 内容可见且媒体 URL 可下载。
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var contentId = (await ReadJson(create)).GetProperty("id").GetString();
        var friendGet = await friendClient.GetAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.OK, friendGet.StatusCode);
        var body = await ReadJson(friendGet);
        var media = body.GetProperty("media");
        Assert.Equal(2, media.GetArrayLength());
        var mediaUrl = media[0].GetProperty("url").GetString()!;
        Assert.StartsWith("/media/", mediaUrl);
        var download = await friendClient.GetAsync(mediaUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);

        // 非好友：内容 404。
        var outsiderClient = _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsiderClient.GetAsync($"/contents/{contentId}")).StatusCode);
    }

    [Fact]
    public async Task Oversized_image_is_compressed_below_limit()
    {
        var author = await _factory.SeedUserAsync("author6b@test.local", "Author6b");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 噪声 PNG 不可压缩，2600x2600 RGB 原始约 20MB > 10MB 默认上限。
        var bigPng = CreateNoisePng(2600, 2600);
        Assert.True(bigPng.Length > 10 * 1024 * 1024);

        var response = await PostFileAsync(client, bigPng, "image/png", "big.png");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal("image", body.GetProperty("kind").GetString());
        Assert.True(body.GetProperty("sizeBytes").GetInt64() < 10 * 1024 * 1024);
        Assert.NotNull(body.GetProperty("thumbUrl").GetString());
    }

    [Fact]
    public async Task Video_over_configured_limit_returns_413()
    {
        var author = await _factory.SeedUserAsync("author6c@test.local", "Author6c");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.SystemConfigs.Add(new SystemConfig { Key = "media.maxVideoBytes", Value = "1024" });
            await db.SaveChangesAsync();
        }

        var payload = new byte[2048];
        Random.Shared.NextBytes(payload);
        var response = await PostFileAsync(client, payload, "video/mp4", "clip.mp4");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Audio_over_configured_limit_returns_413()
    {
        var author = await _factory.SeedUserAsync("author6d@test.local", "Author6d");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.SystemConfigs.Add(new SystemConfig { Key = "media.maxAudioBytes", Value = "1024" });
            await db.SaveChangesAsync();
        }

        var payload = new byte[2048];
        Random.Shared.NextBytes(payload);
        var response = await PostFileAsync(client, payload, "audio/mpeg", "clip.mp3");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Delete_content_cascades_db_rows_and_disk_files()
    {
        var author = await _factory.SeedUserAsync("author6e@test.local", "Author6e");
        var friend = await _factory.SeedUserAsync("friend6e@test.local", "Friend6e");
        var outsider = await _factory.SeedUserAsync("outsider6e@test.local", "Outsider6e");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var media = await UploadImageAsync(authorClient, CreatePng(32, 32));
        var create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "to be deleted",
            recipientIds = new[] { friend.Id },
            mediaIds = new[] { media.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        Assert.Equal(HttpStatusCode.OK,
            (await friendClient.GetAsync($"/contents/{contentId}")).StatusCode);

        var delete = await authorClient.DeleteAsync($"/contents/{contentId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // DB：Content 行与 MediaAsset 行都消失。
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            Assert.False(await db.Contents.AnyAsync(c => c.Id == contentId));
            Assert.False(await db.MediaAssets.AnyAsync(m => m.ContentId == contentId));
        }

        // 磁盘：主文件与缩略图都消失。
        Assert.False(File.Exists(MediaPath(media.Url)));
        Assert.False(File.Exists(MediaPath(media.ThumbUrl!)));

        // 名单内好友再读 → 404；重复删除与外人删除 → 404。
        Assert.Equal(HttpStatusCode.NotFound,
            (await friendClient.GetAsync($"/contents/{contentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await authorClient.DeleteAsync($"/contents/{contentId}")).StatusCode);
        var outsiderClient = _factory.CreateAuthorizedClient(outsider.Id, outsider.Nickname);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsiderClient.DeleteAsync($"/contents/{contentId}")).StatusCode);
    }

    private string MediaPath(string url)
    {
        var storedName = url.Substring("/media/".Length);
        return Path.Combine(_factory.DataDirectory, "media", storedName);
    }

    [Fact]
    public async Task Edit_say_text_via_put_author_only()
    {
        var author = await _factory.SeedUserAsync("author6f@test.local", "Author6f");
        var friend = await _factory.SeedUserAsync("friend6f@test.local", "Friend6f");
        await _factory.SeedFriendshipAsync(author, friend);

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "before edit",
            recipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var edit = await authorClient.PutAsJsonAsync($"/contents/{contentId}", new { text = "edited text" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await ReadJson(edit);
        Assert.Equal("edited text", body.GetProperty("text").GetString());

        // 非作者编辑 → 404。
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        Assert.Equal(HttpStatusCode.NotFound,
            (await friendClient.PutAsJsonAsync($"/contents/{contentId}", new { text = "hijack" })).StatusCode);
    }

    [Fact]
    public async Task Media_endpoints_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/media/whatever.jpg")).StatusCode);

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "file", "x.png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/media", form)).StatusCode);
    }

    [Fact]
    public async Task Orphan_media_readable_by_any_logged_in_user()
    {
        var author = await _factory.SeedUserAsync("author6g@test.local", "Author6g");
        var stranger = await _factory.SeedUserAsync("stranger6g@test.local", "Stranger6g");

        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var media = await UploadImageAsync(authorClient, CreatePng(24, 24));

        // 已知简化：媒体 URL 仅要求登录，不做可见名单校验。
        var strangerClient = _factory.CreateAuthorizedClient(stranger.Id, stranger.Nickname);
        Assert.Equal(HttpStatusCode.OK,
            (await strangerClient.GetAsync(media.Url)).StatusCode);
    }

    [Fact]
    public async Task Invalid_media_bindings_return_400()
    {
        var author = await _factory.SeedUserAsync("author6h@test.local", "Author6h");
        var other = await _factory.SeedUserAsync("other6h@test.local", "Other6h");
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        // 10 张图 → 400（每张 variant 不同：同字节会被上传幂等去重）。
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            ids.Add((await UploadImageAsync(authorClient, CreatePng(16, 16, variant: i))).Id);
        }
        var create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "too many",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = ids,
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // 他人上传的媒体 → 400。
        var foreign = await UploadImageAsync(otherClient, CreatePng(16, 16));
        create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "foreign media",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = new[] { foreign.Id },
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // 已绑定过的媒体 → 400。
        var bound = await UploadImageAsync(authorClient, CreatePng(16, 16));
        create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "first bind",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = new[] { bound.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "second bind",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = new[] { bound.Id },
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // 2 个视频 → 400（字节不同：同字节会被上传幂等去重成同一资产，测不出"两个"）。
        var v1 = await UploadRawAsync(authorClient, new byte[] { 1 }, "video/mp4", "a.mp4");
        var v2 = await UploadRawAsync(authorClient, new byte[] { 2 }, "video/mp4", "b.mp4");
        create = await authorClient.PostAsJsonAsync("/contents/says", new
        {
            text = "two videos",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = new[] { v1.Id, v2.Id },
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Put_media_rebind_unbinds_old_media()
    {
        var author = await _factory.SeedUserAsync("author6i@test.local", "Author6i");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        var old = await UploadImageAsync(client, CreatePng(16, 16));
        var create = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "rebind",
            recipientIds = Array.Empty<Guid>(),
            mediaIds = new[] { old.Id },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var contentId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var replacement = await UploadImageAsync(client, CreatePng(16, 16));
        var edit = await client.PutAsJsonAsync($"/contents/{contentId}", new
        {
            mediaIds = new[] { replacement.Id },
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var media = (await ReadJson(edit)).GetProperty("media");
        Assert.Equal(1, media.GetArrayLength());
        Assert.Equal(replacement.Id, media[0].GetProperty("id").GetGuid());

        // 旧媒体解绑后回到未绑定状态，文件保留。
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var oldAsset = await db.MediaAssets.SingleAsync(m => m.Id == old.Id);
            Assert.Null(oldAsset.ContentId);
        }
        Assert.True(File.Exists(MediaPath(old.Url)));
    }

    private static async Task<MediaUpload> UploadImageAsync(HttpClient client, byte[] bytes)
    {
        return await UploadRawAsync(client, bytes, "image/png", "test.png");
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client, byte[] bytes, string contentType, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, "file", fileName);
        return await client.PostAsync("/media", form);
    }

    private static async Task<MediaUpload> UploadRawAsync(
        HttpClient client, byte[] bytes, string contentType, string fileName)
    {
        var response = await PostFileAsync(client, bytes, contentType, fileName);
        // 201 = 新建；200 = 上传幂等命中同字节未绑定资产（同一测试内重复传同图时复用）
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"unexpected upload status {response.StatusCode}");
        var body = await ReadJson(response);
        return new MediaUpload(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("url").GetString()!,
            body.GetProperty("thumbUrl").GetString());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static byte[] CreatePng(int width, int height, int variant = 0)
    {
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // variant 使同尺寸图字节不同（上传按内容幂等去重，同字节会复用同一资产）
                    row[x] = new Rgb24((byte)(x * 4 + variant), (byte)(y * 4), 128);
                }
            }
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateNoisePng(int width, int height)
    {
        var rng = new Random(42);
        var pixels = new Rgb24[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        }
        using var image = Image.LoadPixelData<Rgb24>(pixels, width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private sealed record MediaUpload(Guid Id, string Url, string? ThumbUrl);
}
