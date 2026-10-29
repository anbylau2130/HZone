using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 账号级播放列表：双音源添加（url 外链 / media 本人音频资产）、列表、整体重排、删除、跨用户隔离，
/// 以及 /me/media 音频库列表。
/// </summary>
public class PlaylistTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public PlaylistTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Url_item_crud_and_reorder()
    {
        var user = await _factory.SeedUserAsync("pl1@test.local", "Pl1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 添加两个外链项（标题必填、必须 http(s)）。
        var a = await AddUrlAsync(client, "晨间播客", "https://example.com/a.mp3");
        var b = await AddUrlAsync(client, "夜间电台", "https://example.com/b.mp3");
        Assert.Equal(1, a.GetProperty("sortOrder").GetInt32());
        Assert.Equal(2, b.GetProperty("sortOrder").GetInt32());

        // 非 http(s) 直链 → 400；无标题 → 400。
        var badUrl = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "url", title = "x", url = "ftp://example.com/a.mp3" });
        Assert.Equal(HttpStatusCode.BadRequest, badUrl.StatusCode);
        var noTitle = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "url", url = "https://example.com/c.mp3" });
        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);

        // 整体重排 b→a。
        var reorder = await client.PutAsJsonAsync("/me/playlist/reorder",
            new { ids = new[] { b.GetProperty("id").GetGuid(), a.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.Equal("夜间电台", list.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(1, list.GetProperty("items")[0].GetProperty("sortOrder").GetInt32());

        // 重排序列与现集合不一致 → 400。
        var mismatch = await client.PutAsJsonAsync("/me/playlist/reorder",
            new { ids = new[] { a.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        // 删除后再列表。
        var del = await client.DeleteAsync($"/me/playlist/{a.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(1, after.GetProperty("total").GetInt32());
        Assert.Equal("夜间电台", after.GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Media_item_references_own_audio_and_returns_stored_name()
    {
        var user = await _factory.SeedUserAsync("pl2@test.local", "Pl2");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 上传一个音频资产（ffmpeg 不可用时小文件原样入库，kind=audio）。
        var asset = await UploadAudioAsync(client, "demo.mp3");
        var assetId = asset.GetProperty("id").GetGuid();

        // /me/media 音频库：能看到刚上传的资产。
        var lib = await client.GetFromJsonAsync<JsonElement>("/me/media");
        Assert.Equal(1, lib.GetProperty("total").GetInt32());
        Assert.Equal(assetId, lib.GetProperty("items")[0].GetProperty("id").GetGuid());

        // 加入播放列表：标题缺省用资产原名，响应/列表带 storedName 供前端拼播放路径。
        var added = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var item = await added.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("demo.mp3", item.GetProperty("title").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(asset.GetProperty("url").GetString(),
            "/media/" + list.GetProperty("items")[0].GetProperty("mediaStoredName").GetString());

        // 引用他人资产 / 不存在资产 → 404。
        var other = await _factory.SeedUserAsync("pl3@test.local", "Pl3");
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);
        var stolen = await otherClient.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Items_are_isolated_per_user()
    {
        var alice = await _factory.SeedUserAsync("pl4@test.local", "Pl4");
        var bob = await _factory.SeedUserAsync("pl5@test.local", "Pl5");
        var aliceClient = _factory.CreateAuthorizedClient(alice.Id, alice.Nickname);
        var bobClient = _factory.CreateAuthorizedClient(bob.Id, bob.Nickname);

        var a = await AddUrlAsync(aliceClient, "只属于 Alice", "https://example.com/x.mp3");
        var aliceId = a.GetProperty("id").GetGuid();

        // Bob 看不到 Alice 的条目，也删不掉（统一 404）。
        var bobList = await bobClient.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(0, bobList.GetProperty("total").GetInt32());
        var del = await bobClient.DeleteAsync($"/me/playlist/{aliceId}");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);

        var aliceList = await aliceClient.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Equal(1, aliceList.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Server_music_library_admin_config_and_streaming()
    {
        var admin = await _factory.EnsureAdminAsync();
        var user = await _factory.SeedUserAsync("pl7@test.local", "Pl7");
        var adminClient = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var userClient = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 未配置：回落默认音乐根（data/music，上传音频的落盘目录），files 视目录内容。
        var empty = await userClient.GetFromJsonAsync<JsonElement>("/me/player/server-library");
        Assert.NotNull(empty.GetProperty("musicDir").GetString());

        // 非 admin 配置 → 403。
        var denied = await userClient.PutAsJsonAsync("/player/music-dir", new { musicDir = "/tmp" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // admin 配置临时目录：2 音频 + 1 jpg（jpg 不进清单），文件名排序。
        var dir = Path.Combine(Path.GetTempPath(), $"hzone-music-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "b-track.mp3"), "fake-mp3-payload");
            await File.WriteAllTextAsync(Path.Combine(dir, "a-track.wav"), "fake-wav");
            await File.WriteAllTextAsync(Path.Combine(dir, "cover.jpg"), "not-audio");

            var set = await adminClient.PutAsJsonAsync("/player/music-dir", new { musicDir = dir });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);

            var lib = await userClient.GetFromJsonAsync<JsonElement>("/me/player/server-library");
            Assert.Equal(dir, lib.GetProperty("musicDir").GetString());
            Assert.Equal(2, lib.GetProperty("files").GetArrayLength());
            Assert.Equal("a-track.wav", lib.GetProperty("files")[0].GetProperty("name").GetString());

            // 文件下发：字节正确 + Content-Type 按扩展名；路径穿越与不存在名被拒。
            var file = await userClient.GetAsync($"/player/file/{Uri.EscapeDataString("a-track.wav")}");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal("audio/wav", file.Content.Headers.ContentType?.MediaType);
            Assert.Equal("fake-wav", await file.Content.ReadAsStringAsync());
            var bad = await userClient.GetAsync("/player/file/..%2Fhzone.db");
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var missing = await userClient.GetAsync("/player/file/nope.mp3");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            // 目录不存在 → 400。
            var badDir = await adminClient.PutAsJsonAsync("/player/music-dir", new { musicDir = "/definitely/not/here" });
            Assert.Equal(HttpStatusCode.BadRequest, badDir.StatusCode);

            // 空串清除：回落默认音乐根（data/music）。
            var clear = await adminClient.PutAsJsonAsync("/player/music-dir", new { musicDir = "" });
            Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
            var after = await userClient.GetFromJsonAsync<JsonElement>("/me/player/server-library");
            Assert.NotEqual(dir, after.GetProperty("musicDir").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<JsonElement> AddUrlAsync(HttpClient client, string title, string url)
    {
        var res = await client.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "url", title, url });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> UploadAudioAsync(HttpClient client, string fileName)
    {
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent(new byte[] { 0x49, 0x44, 0x33, 0x04 }); // 任意字节：无 ffmpeg 时按上限原样存
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        var res = await client.PostAsync("/media", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }
}
