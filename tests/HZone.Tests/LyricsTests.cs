using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 歌词（lrc 原文）：录入/覆盖/清除 + 播放列表带出；仅本人条目。
/// </summary>
public class LyricsTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public LyricsTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Set_list_and_clear_lyrics_roundtrip()
    {
        var user = await _factory.SeedUserAsync("lyr1@test.local", "Lyr1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var assetId = await UploadAudioAsync(client, "with-lrc.mp3");
        var added = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var itemId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var lrc = "[00:01.50]第一行\n[00:04.00]第二行\n[01:02.30]尾声\n";
        var set = await client.PutAsJsonAsync($"/me/playlist/{itemId}/lyrics", new { lyrics = lrc });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setBody = await set.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(setBody.GetProperty("hasLyrics").GetBoolean());

        // 列表带出原文
        var pl = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        var item = Assert.Single(pl.GetProperty("items").EnumerateArray());
        Assert.Equal(lrc.Trim(), item.GetProperty("lyrics").GetString());

        // 清除（空串）→ null
        var clear = await client.PutAsJsonAsync($"/me/playlist/{itemId}/lyrics", new { lyrics = "" });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var pl2 = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Null(Assert.Single(pl2.GetProperty("items").EnumerateArray())
            .GetProperty("lyrics").GetString());

        // 非本人/不存在 404；超长 400
        var other = await _factory.SeedUserAsync("lyr2@test.local", "Lyr2");
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherClient.PutAsJsonAsync($"/me/playlist/{itemId}/lyrics",
                new { lyrics = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/me/playlist/{itemId}/lyrics",
                new { lyrics = new string('x', 100_001) })).StatusCode);
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
