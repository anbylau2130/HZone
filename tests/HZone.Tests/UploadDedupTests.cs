using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 上传幂等去重：同一文件（同字节）多次上传 → 复用同一条未绑定资产（不重复落盘）；
/// 已绑定内容的资产不复用照常新建；不同人各自保留；播放列表同一资产不重复入列。
/// </summary>
public class UploadDedupTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public UploadDedupTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Same_file_uploaded_twice_returns_same_unbound_asset()
    {
        var user = await _factory.SeedUserAsync("dp7a@test.local", "Dp7a");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var first = await UploadAsync(client, "same-tune.mp3");
        Assert.Equal(HttpStatusCode.Created, first.Status);
        var second = await UploadAsync(client, "renamed-copy.mp3"); // 文件名不同、字节相同
        Assert.Equal(HttpStatusCode.OK, second.Status); // 幂等复用 → 200 + 已有资产
        Assert.Equal(first.Id, second.Id);

        // 音频库只一条
        var lib = await client.GetFromJsonAsync<JsonElement>("/me/media");
        Assert.Equal(1, lib.GetProperty("total").GetInt32());
        Assert.Equal("same-tune.mp3", lib.GetProperty("items")[0].GetProperty("originalName").GetString());

        // 播放列表：加同一资产两次 → 第二次 409
        var added = await client.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = first.Id });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var dup = await client.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = first.Id });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Bound_asset_is_not_reused_and_other_users_are_isolated()
    {
        var user = await _factory.SeedUserAsync("dp7b@test.local", "Dp7b");
        var other = await _factory.SeedUserAsync("dp7c@test.local", "Dp7c");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        // 上传 → 绑进随记（ContentId 非空）
        var a1 = await UploadAsync(client, "clip.mp3");
        var created = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "带音频", recipientIds = Array.Empty<Guid>(), mediaIds = new[] { a1.Id },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // 同字节再传：已绑定不复用 → 新建（Created，不同 id）
        var a2 = await UploadAsync(client, "clip.mp3");
        Assert.Equal(HttpStatusCode.Created, a2.Status);
        Assert.NotEqual(a1.Id, a2.Id);

        // 他人同字节：各自保留（隔离）
        var b1 = await UploadAsync(otherClient, "clip.mp3");
        Assert.Equal(HttpStatusCode.Created, b1.Status);
        Assert.NotEqual(a1.Id, b1.Id);
        var otherLib = await otherClient.GetFromJsonAsync<JsonElement>("/me/media");
        Assert.Equal(1, otherLib.GetProperty("total").GetInt32());
    }

    private sealed record Uploaded(HttpStatusCode Status, Guid Id);

    private static async Task<Uploaded> UploadAsync(HttpClient client, string fileName)
    {
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent([0x49, 0x44, 0x33, 0x04, 0x10, 0x20]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        var res = await client.PostAsync("/media", form);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return new Uploaded(res.StatusCode, body.GetProperty("id").GetGuid());
    }
}
