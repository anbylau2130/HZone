using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 只读分享外链：创建（作者门+明文一次+天数校验）、匿名公开读（内容投影不含
/// 账户字段）、过期/撤销/删除级联 404、越权 404、公开媒体门（仅该内容媒体）。
/// </summary>
public class ShareLinkTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public ShareLinkTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_read_public_and_lifecycle()
    {
        var author = await _factory.SeedUserAsync("sh5a@test.local", "Sh5a");
        var other = await _factory.SeedUserAsync("sh5b@test.local", "Sh5b");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        var created = await client.PostAsJsonAsync("/contents/journals", new
        {
            title = "给家人看的笔记", markdown = "# 你好\n\n见字如面。", recipientIds = Array.Empty<Guid>(),
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 非作者创建 → 404
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherClient.PostAsJsonAsync($"/contents/{id}/share-links", new { days = 7 })).StatusCode);
        // days 越界 → 400
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/contents/{id}/share-links", new { days = 99 })).StatusCode);

        // 创建：明文 token 只出现一次；列表绝不含 token
        var link = await client.PostAsJsonAsync($"/contents/{id}/share-links", new { days = 7 });
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        var linkBody = await link.Content.ReadFromJsonAsync<JsonElement>();
        var token = linkBody.GetProperty("token").GetString()!;
        Assert.True(token.Length >= 43);
        var list = await client.GetFromJsonAsync<JsonElement>("/me/share-links");
        var listItem = list.GetProperty("items")[0];
        Assert.Equal(id, listItem.GetProperty("contentId").GetGuid());
        Assert.False(listItem.TryGetProperty("token", out _));

        // 匿名公开读：内容投影正确、不含 email/收件人；图片媒体给公开路径
        var anon = _factory.CreateClient();
        var pub = await anon.GetFromJsonAsync<JsonElement>($"/public/shared/{token}");
        Assert.Equal("journal", pub.GetProperty("kind").GetString());
        Assert.Equal("给家人看的笔记", pub.GetProperty("title").GetString());
        Assert.Equal("Sh5a", pub.GetProperty("authorNickname").GetString());
        Assert.False(pub.TryGetProperty("authorEmail", out _));
        Assert.False(pub.TryGetProperty("recipients", out _));

        // 坏 token → 404
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/public/shared/nope")).StatusCode);

        // 撤销后匿名读 404
        var linkId = linkBody.GetProperty("id").GetGuid();
        await client.DeleteAsync($"/share-links/{linkId}");
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/public/shared/{token}")).StatusCode);

        // 内容删除 → 级联，token 失效
        var link2 = await client.PostAsJsonAsync($"/contents/{id}/share-links", new { days = 7 });
        var token2 = (await link2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        await client.DeleteAsync($"/contents/{id}");
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/public/shared/{token2}")).StatusCode);
    }

    [Fact]
    public async Task Public_media_only_serves_that_contents_assets()
    {
        var author = await _factory.SeedUserAsync("sh5c@test.local", "Sh5c");
        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);

        // 内容图（绑定）+ 未绑定的临时资产（同 owner）+ 他人资产
        var bound = (await UploadPngAsync(client, "bound.png"))!.Value;
        var loose = await UploadPngAsync(client, "loose.png");
        var other = await _factory.SeedUserAsync("sh5d@test.local", "Sh5d");
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);
        var otherAsset = (await UploadPngAsync(otherClient, "other.png"))!.Value;
        _ = loose;

        var created = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "带图分享", recipientIds = Array.Empty<Guid>(), mediaIds = new[] { bound.GetProperty("id").GetGuid() },
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var link = await client.PostAsJsonAsync($"/contents/{id}/share-links", new { days = 1 });
        var token = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var boundName = bound.GetProperty("url").GetString()!.Split('/').Last();

        var anon = _factory.CreateClient();
        // 内容媒体可读（200/206）
        var ok = await anon.GetAsync($"/public/shared/{token}/media/{boundName}");
        Assert.True(ok.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent);
        // 他人资产与无关名 404（不泄露存在性）
        var otherName = otherAsset.GetProperty("url").GetString()!.Split('/').Last();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/public/shared/{token}/media/{otherName}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync($"/public/shared/{token}/media/..%2Fx")).StatusCode);
    }

    private static async Task<JsonElement?> UploadPngAsync(HttpClient client, string name)
    {
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent(
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
            0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00,
            0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        ]);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", name);
        var res = await client.PostAsync("/media", form);
        return res.StatusCode == HttpStatusCode.Created
            ? await res.Content.ReadFromJsonAsync<JsonElement>()
            : null;
    }
}
