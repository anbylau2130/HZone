using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 音频元数据（上传时前端探测随表单上送）：落库、/me/media 与 /me/playlist 带出、
/// 脏值/缺省静默为 null（元数据是展示提示，绝不阻断上传）。
/// </summary>
public class AudioMetadataTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public AudioMetadataTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upload_with_metadata_persists_and_surfaces_everywhere()
    {
        var user = await _factory.SeedUserAsync("meta1@test.local", "Meta1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var assetId = await UploadAudioWithMetaAsync(client, "night-drive.mp3",
            artist: "City Lights", durationSeconds: 213.4, bitrateKbps: 320);

        // 音频库列表带出全部元数据
        var lib = await client.GetFromJsonAsync<JsonElement>("/me/media");
        var row = Assert.Single(lib.GetProperty("items").EnumerateArray().Where(
            i => i.GetProperty("id").GetGuid() == assetId));
        Assert.Equal("City Lights", row.GetProperty("artist").GetString());
        Assert.Equal(213.4, row.GetProperty("durationSeconds").GetDouble());
        Assert.Equal(320, row.GetProperty("bitrateKbps").GetInt32());

        // 入列后播放列表也带出（含来源文件名与大小）
        var added = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var pl = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        var item = Assert.Single(pl.GetProperty("items").EnumerateArray());
        Assert.Equal("City Lights", item.GetProperty("artist").GetString());
        Assert.Equal(213.4, item.GetProperty("durationSeconds").GetDouble());
        Assert.Equal(320, item.GetProperty("bitrateKbps").GetInt32());
        Assert.True(item.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.Equal("night-drive.mp3", item.GetProperty("originalFileName").GetString());
    }

    [Fact]
    public async Task Garbage_or_missing_metadata_is_dropped_not_rejected()
    {
        var user = await _factory.SeedUserAsync("meta2@test.local", "Meta2");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 脏值：超长艺术家/负时长/超界比特率 → 全部置 null，上传照常成功
        var assetId = await UploadAudioWithMetaAsync(client, "dirty.mp3",
            artist: new string('x', 500), durationSeconds: -5, bitrateKbps: 999999);
        var lib = await client.GetFromJsonAsync<JsonElement>("/me/media");
        var row = Assert.Single(lib.GetProperty("items").EnumerateArray().Where(
            i => i.GetProperty("id").GetGuid() == assetId));
        Assert.Null(row.GetProperty("artist").GetString());
        Assert.Null(row.GetProperty("durationSeconds").GetString());
        Assert.Null(row.GetProperty("bitrateKbps").GetString());

        // 不带任何元数据（老客户端/探测失败）→ null，不影响上传
        var bareId = await UploadAudioWithMetaAsync(client, "bare.mp3");
        var libTwo = await client.GetFromJsonAsync<JsonElement>("/me/media");
        var bare = Assert.Single(libTwo.GetProperty("items").EnumerateArray().Where(
            i => i.GetProperty("id").GetGuid() == bareId));
        Assert.Null(bare.GetProperty("artist").GetString());
        Assert.Null(bare.GetProperty("durationSeconds").GetString());
        Assert.Null(bare.GetProperty("bitrateKbps").GetString());
    }

    private static async Task<Guid> UploadAudioWithMetaAsync(
        HttpClient client, string fileName,
        string? artist = null, double? durationSeconds = null, int? bitrateKbps = null)
    {
        // 造数用不同内容字节：同字节会被幂等去重命中复用旧资产（无元数据）。
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent(payload);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        if (artist is not null) form.Add(new StringContent(artist), "artist");
        if (durationSeconds is not null)
            form.Add(new StringContent(durationSeconds.Value.ToString()), "durationSeconds");
        if (bitrateKbps is not null)
            form.Add(new StringContent(bitrateKbps.Value.ToString()), "bitrateKbps");
        var res = await client.PostAsync("/media", form);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"upload failed: {res.StatusCode}");
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
