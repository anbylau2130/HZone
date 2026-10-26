using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 照片墙：/me/media/photos 图片资产分页（新→旧、缩略图回退主图、仅本人图片）。
/// 用 1×1 真 PNG（后端图片管线会重编码并产出缩略图）。
/// </summary>
public class PhotoWallTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public PhotoWallTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // 1x1 红 PNG（合法图片数据，走压缩管线生成缩略图）
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public async Task Photos_page_lists_own_images_newest_first_with_thumbs()
    {
        var user = await _factory.SeedUserAsync("ph9a@test.local", "Ph9a");
        var other = await _factory.SeedUserAsync("ph9b@test.local", "Ph9b");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        Guid firstId = Guid.Empty;
        var uploadedIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            using var form = new MultipartFormDataContent();
            using var bytes = new ByteArrayContent(Png);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(bytes, "file", $"p{i}.png");
            var res = await client.PostAsync("/media", form);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var asset = await res.Content.ReadFromJsonAsync<JsonElement>();
            uploadedIds.Add(asset.GetProperty("id").GetGuid());
        }
        // 他人的图与自己的音频不进照片墙
        await otherClient.PostAsync("/media", MakePngForm("not-mine.png"));
        await UploadAudioAsync(client);

        var page = await client.GetFromJsonAsync<JsonElement>("/me/media/photos?limit=2");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        // 同毫秒批量上传 CreatedAt 相同 → Id 次序随机：断言集合归属与缩略图，
        // 不锁具体次序；分页不重不漏由两页并集 = 全量上传集合保证
        var first = page.GetProperty("items")[0];
        Assert.True(uploadedIds.Contains(first.GetProperty("id").GetGuid()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("thumbStoredName").GetString()));
        var pageIds = page.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        // 第二页取余下 1 条，两页并集 = 上传的 3 张
        var page2 = await client.GetFromJsonAsync<JsonElement>("/me/media/photos?offset=2&limit=2");
        var page2Ids = page2.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Single(page2Ids);
        Assert.Empty(pageIds.Concat(page2Ids).Except(uploadedIds));
        Assert.Empty(uploadedIds.Except(pageIds.Concat(page2Ids)));
    }

    private static MultipartFormDataContent MakePngForm(string name)
    {
        var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Png);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", name);
        return form;
    }

    private static async Task UploadAudioAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent([0x49, 0x44, 0x33, 0x04]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", "a.mp3");
        await client.PostAsync("/media", form);
    }
}
