using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 音乐目录体系：上传音频选共享/私有 → 物理落盘 music 根（共享）/ music/{昵称}/（私有）；
/// 音频库含共享资产（他人可见可加列表）；删除资产级联清列表引用与物理文件；
/// server-library 不重复列已登记文件。
/// </summary>
public class MusicFolderTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public MusicFolderTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upload_shared_and_private_lands_in_music_dirs_and_cross_visible()
    {
        var alice = await _factory.SeedUserAsync("mf1@test.local", "乐手甲");
        var bob = await _factory.SeedUserAsync("mf2@test.local", "乐手乙");
        var aliceClient = _factory.CreateAuthorizedClient(alice.Id, alice.Nickname);
        var bobClient = _factory.CreateAuthorizedClient(bob.Id, bob.Nickname);

        // Alice 上传共享 + 私有各一首
        var sharedId = await UploadAudioAsync(aliceClient, "共享曲.mp3", shared: true);
        var privateId = await UploadAudioAsync(aliceClient, "私曲.mp3", shared: false);

        // 落库标记正确
        var aliceLib = await aliceClient.GetFromJsonAsync<JsonElement>("/me/media");
        var sharedRow = aliceLib.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetGuid() == sharedId);
        var privateRow = aliceLib.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetGuid() == privateId);
        Assert.True(sharedRow.GetProperty("isShared").GetBoolean());
        Assert.Equal("@music", sharedRow.GetProperty("subdir").GetString());
        Assert.False(privateRow.GetProperty("isShared").GetBoolean());
        Assert.Equal("@music/乐手甲", privateRow.GetProperty("subdir").GetString());

        // 物理文件落对应目录（默认音乐根 = data/music）
        var musicRoot = Path.Combine("data", "music");
        Assert.True(Directory.Exists(musicRoot), "music root should exist");
        Assert.True(File.Exists(Path.Combine(musicRoot, sharedRow.GetProperty("storedName").GetString()!)),
            "shared file at music root");
        Assert.True(File.Exists(Path.Combine(musicRoot, "乐手甲", privateRow.GetProperty("storedName").GetString()!)),
            "private file at music/{nick}");

        // Bob 的音频库：能看到 Alice 的共享曲，看不到私有曲
        var bobLib = await bobClient.GetFromJsonAsync<JsonElement>("/me/media");
        var ids = bobLib.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(sharedId, ids);
        Assert.DoesNotContain(privateId, ids);
        var bobSeesShared = bobLib.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetGuid() == sharedId);
        Assert.False(bobSeesShared.GetProperty("mine").GetBoolean());
        Assert.True(bobSeesShared.GetProperty("isShared").GetBoolean());

        // Bob 把 Alice 的共享曲加入自己的播放列表 → 成功
        var added = await bobClient.PostAsJsonAsync("/me/playlist",
            new { sourceKind = "media", mediaAssetId = sharedId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // /media/{storedName} 能取到（目录解析正确）
        var stream = await bobClient.GetAsync($"/media/{sharedRow.GetProperty("storedName").GetString()}");
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
    }

    [Fact]
    public async Task Delete_asset_cascades_playlist_refs_and_physical_file()
    {
        var alice = await _factory.SeedUserAsync("mf3@test.local", "乐手丙");
        var bob = await _factory.SeedUserAsync("mf4@test.local", "乐手丁");
        var aliceClient = _factory.CreateAuthorizedClient(alice.Id, alice.Nickname);
        var bobClient = _factory.CreateAuthorizedClient(bob.Id, bob.Nickname);

        var sharedId = await UploadAudioAsync(aliceClient, "将删曲.mp3", shared: true);
        var lib = await aliceClient.GetFromJsonAsync<JsonElement>("/me/media");
        var row = lib.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetGuid() == sharedId);
        var storedName = row.GetProperty("storedName").GetString()!;
        var filePath = Path.Combine("data", "music", storedName);
        Assert.True(File.Exists(filePath));

        // 两人都加入列表
        await aliceClient.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = sharedId });
        await bobClient.PostAsJsonAsync("/me/playlist", new { sourceKind = "media", mediaAssetId = sharedId });

        // 非上传者删 → 404
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.DeleteAsync($"/me/media/{sharedId}")).StatusCode);

        // 上传者删 → 资产 + 双方列表项 + 物理文件全清
        Assert.Equal(HttpStatusCode.OK, (await aliceClient.DeleteAsync($"/me/media/{sharedId}")).StatusCode);
        Assert.False(File.Exists(filePath), "physical file deleted");
        var aliceList = await aliceClient.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Empty(aliceList.GetProperty("items").EnumerateArray());
        var bobList = await bobClient.GetFromJsonAsync<JsonElement>("/me/playlist");
        Assert.Empty(bobList.GetProperty("items").EnumerateArray());

        // 音频库不再返回
        var libAfter = await aliceClient.GetFromJsonAsync<JsonElement>("/me/media");
        Assert.DoesNotContain(sharedId, libAfter.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()));
    }

    private static async Task<Guid> UploadAudioAsync(HttpClient client, string fileName, bool shared)
    {
        var payload = new byte[128];
        Random.Shared.NextBytes(payload);
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent(payload);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(bytes, "file", fileName);
        form.Add(new StringContent(shared ? "true" : "false"), "shared");
        var res = await client.PostAsync("/media", form);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"upload failed: {res.StatusCode}");
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
