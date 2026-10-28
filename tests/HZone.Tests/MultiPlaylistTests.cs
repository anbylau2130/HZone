using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 多歌单：列表/新建/改名/删除 + 条目归属与默认歌单惰性兜底 + 越权与限额。
/// </summary>
public class MultiPlaylistTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public MultiPlaylistTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Playlist_crud_and_item_scoping()
    {
        var user = await _factory.SeedUserAsync("mp1@test.local", "Mp1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 新用户 GET /me/playlists：惰性建默认歌单
        var lists = await client.GetFromJsonAsync<JsonElement>("/me/playlists");
        var def = Assert.Single(lists.GetProperty("items").EnumerateArray());
        Assert.Equal("默认歌单", def.GetProperty("name").GetString());
        Assert.Equal(0, def.GetProperty("itemCount").GetInt32());
        var defaultId = def.GetProperty("id").GetGuid();

        // 新建两张 + 改名
        var created = await client.PostAsJsonAsync("/me/playlists", new { name = "深夜" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var nightId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/me/playlists/{nightId}", new { name = "深夜电台" })).StatusCode);

        // 条目归属：同一资产可入两张不同歌单；GET 按 playlistId 过滤
        var assetId = await UploadAudioAsync(client, "song.mp3");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId, playlistId = defaultId })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId, playlistId = nightId })).StatusCode);
        // 同歌单重复 → 409
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId, playlistId = defaultId })).StatusCode);

        var inDefault = await client.GetFromJsonAsync<JsonElement>($"/me/playlist?playlistId={defaultId}");
        Assert.Equal(1, inDefault.GetProperty("items").GetArrayLength());
        Assert.Equal(defaultId, inDefault.GetProperty("playlistId").GetGuid());
        Assert.Equal("默认歌单", inDefault.GetProperty("playlistName").GetString());
        var inNight = await client.GetFromJsonAsync<JsonElement>($"/me/playlist?playlistId={nightId}");
        Assert.Equal(1, inNight.GetProperty("items").GetArrayLength());

        // 列表条目数正确
        var lists2 = await client.GetFromJsonAsync<JsonElement>("/me/playlists");
        var counts = lists2.GetProperty("items").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString(), p => p.GetProperty("itemCount").GetInt32());
        Assert.Equal(1, counts["默认歌单"]);
        Assert.Equal(1, counts["深夜电台"]);

        // 删除歌单 → 条目随删；其它歌单不受影响
        Assert.Equal(HttpStatusCode.OK,
            (await client.DeleteAsync($"/me/playlists/{nightId}")).StatusCode);
        var inDefault2 = await client.GetFromJsonAsync<JsonElement>($"/me/playlist?playlistId={defaultId}");
        Assert.Equal(1, inDefault2.GetProperty("items").GetArrayLength());
        var lists3 = await client.GetFromJsonAsync<JsonElement>("/me/playlists");
        Assert.Single(lists3.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Foreign_playlist_and_name_validation_and_limit()
    {
        var user = await _factory.SeedUserAsync("mp2@test.local", "Mp2");
        var other = await _factory.SeedUserAsync("mp3@test.local", "Mp3");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        // 他人歌单：GET 条目/改名/删除统一 404
        var otherLists = await otherClient.GetFromJsonAsync<JsonElement>("/me/playlists");
        var otherId = otherLists.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/me/playlist?playlistId={otherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/me/playlists/{otherId}", new { name = "偷改" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/me/playlists/{otherId}")).StatusCode);

        // 名称为空/超长 → 400
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/me/playlists", new { name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/me/playlists", new { name = new string('x', 51) })).StatusCode);

        // 上限：先触发默认歌单（共 1），再建 19 张成功，第 20 张 409
        await client.GetFromJsonAsync<JsonElement>("/me/playlists");
        for (var i = 0; i < 19; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/me/playlists",
                new { name = $"歌单{i}" })).StatusCode);
        }
        var over = await client.PostAsJsonAsync("/me/playlists", new { name = "超限" });
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    private static async Task<Guid> UploadAudioAsync(HttpClient client, string fileName)
    {
        var payload = new byte[32];
        Random.Shared.NextBytes(payload);
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent(payload);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        var res = await client.PostAsync("/media", form);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
