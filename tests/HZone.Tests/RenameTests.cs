using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 重命名：媒体资产（仅本人、只改显示名，音频库/附件下载名同步）与
/// 播放列表项歌名（独立快照，与资产名解耦）。
/// </summary>
public class RenameTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public RenameTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rename_media_asset_updates_display_name()
    {
        var user = await _factory.SeedUserAsync("rn6a@test.local", "Rn6a");
        var other = await _factory.SeedUserAsync("rn6b@test.local", "Rn6b");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        var assetId = await UploadAudioAsync(client, "raw-uploaded-name.mp3");

        // 他人改名 → 404（不泄露存在性）
        var denied = await otherClient.PutAsJsonAsync($"/me/media/{assetId}/name", new { name = "偷改" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // 本人改名 → 音频库/照片墙显示新名
        var renamed = await client.PutAsJsonAsync($"/me/media/{assetId}/name", new { name = "深夜电台 第一期" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var lib = await client.GetFromJsonAsync<JsonElement>("/me/media");
        Assert.Equal("深夜电台 第一期", lib.GetProperty("items")[0].GetProperty("originalName").GetString());

        // 空/超长 → 400
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/me/media/{assetId}/name", new { name = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/me/media/{assetId}/name", new { name = new string('长', 201) })).StatusCode);
    }

    [Fact]
    public async Task Rename_playlist_item_title_is_independent_snapshot()
    {
        var user = await _factory.SeedUserAsync("rn6c@test.local", "Rn6c");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var assetId = await UploadAudioAsync(client, "old-name.mp3");
        var added = await client.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = assetId });
        var itemId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 资产改名后：列表项仍是旧快照（解耦），新加项取新名
        await client.PutAsJsonAsync($"/me/media/{assetId}/name", new { name = "资产新名" });
        var listAfterAssetRename = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal("old-name.mp3", listAfterAssetRename.GetProperty("items")[0].GetProperty("title").GetString());
        await client.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = assetId });
        var listTwo = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal("资产新名", listTwo.GetProperty("items")[1].GetProperty("title").GetString());

        // 列表项改名 → 显示更新
        var renamed = await client.PutAsJsonAsync($"/me/playlist/{itemId}/title", new { title = "我的深夜歌" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal("我的深夜歌", list.GetProperty("items")[0].GetProperty("title").GetString());

        // 空 → 400
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/me/playlist/{itemId}/title", new { title = "" })).StatusCode);
    }

    private static async Task<Guid> UploadAudioAsync(HttpClient client, string fileName)
    {
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent([0x49, 0x44, 0x33, 0x04]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        var res = await client.PostAsync("/media", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return ((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }
}
