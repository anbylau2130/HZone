using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace HZone.Tests;

/// <summary>
/// 管理员改媒体上限 → 上传行为随之变化（无 ffmpeg 路径为默认，强制指定保证确定性）。
/// </summary>
public class AdminMediaLimitTests : IClassFixture<NoFFmpegApiFactory>
{
    private readonly NoFFmpegApiFactory _fixture;

    public AdminMediaLimitTests(NoFFmpegApiFactory fixture) => _fixture = fixture;

    private HZoneApiFactory Factory => _fixture.Factory;

    [Fact]
    public async Task Lowered_image_limit_rejects_upload_that_cannot_compress_further()
    {
        var admin = await Factory.EnsureAdminAsync();
        var adminClient = Factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var user = await Factory.SeedUserAsync("limit-img@test.local", "LimitImg");
        var client = Factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 噪声图压缩后仍远大于 1KB（每张尺寸不同——上传按内容幂等去重，同字节会复用）
        // 默认 10MB → 201。
        Assert.Equal(HttpStatusCode.Created,
            (await PostFileAsync(client, CreateNoisePng(128, 128), "image/png")).StatusCode);

        // 上限降到 1KB → 压缩后仍超限 → 413。
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync("/admin/config",
            new { mediaLimits = new { imageBytes = 1024L } })).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await PostFileAsync(client, CreateNoisePng(129, 128), "image/png")).StatusCode);

        // 上限调回 10MB → 新图恢复 201。
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync("/admin/config",
            new { mediaLimits = new { imageBytes = 10L * 1024 * 1024 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await PostFileAsync(client, CreateNoisePng(130, 128), "image/png")).StatusCode);
    }

    [Fact]
    public async Task Lowered_video_and_audio_limits_reject_without_ffmpeg()
    {
        var admin = await Factory.EnsureAdminAsync();
        var adminClient = Factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var user = await Factory.SeedUserAsync("limit-va@test.local", "LimitVa");
        var client = Factory.CreateAuthorizedClient(user.Id, user.Nickname);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync("/admin/config",
            new { mediaLimits = new { videoBytes = 1L, audioBytes = 1L } })).StatusCode);

        var payload = new byte[2048];
        Random.Shared.NextBytes(payload);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await PostFileAsync(client, payload, "video/mp4")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await PostFileAsync(client, payload, "audio/mpeg")).StatusCode);
    }

    [Fact]
    public async Task Within_limit_video_stores_directly()
    {
        var admin = await Factory.EnsureAdminAsync();
        var admin2 = await Factory.EnsureAdminAsync();
        var adminClient2 = Factory.CreateAuthorizedClient(admin2.Id, admin2.Nickname);
        var user = await Factory.SeedUserAsync("limit-ok@test.local", "LimitOk");
        var client = Factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 测试顺序无关：先把上限调大（前序测试可能把它压到 1 字节）。
        Assert.Equal(HttpStatusCode.OK, (await adminClient2.PutAsJsonAsync("/admin/config",
            new { mediaLimits = new { videoBytes = 1024L } })).StatusCode);

        var payload = new byte[] { 1, 2, 3 };
        var response = await PostFileAsync(client, payload, "video/mp4");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("video", body.GetProperty("kind").GetString());
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client, byte[] bytes, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, "file", "upload.bin");
        return await client.PostAsync("/media", form);
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
}
