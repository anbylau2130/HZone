using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// /admin/config 访问控制：匿名 401、非管理员 403、未初始化 409、默认值读取。
/// </summary>
public class AdminConfigAccessTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public AdminConfigAccessTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_gets_401()
    {
        await _factory.SeedSetupAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync("/admin/config")).StatusCode);
    }

    [Fact]
    public async Task Non_admin_gets_403()
    {
        await _factory.SeedSetupAsync();
        var user = await _factory.SeedUserAsync("plain6@test.local", "Plain");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/admin/config", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_gets_default_config()
    {
        var admin = await _factory.EnsureAdminAsync();
        var client = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);

        var body = await client.GetFromJsonAsync<JsonElement>("/admin/config");
        Assert.True(body.GetProperty("inviteEnabled").GetBoolean());
        var limits = body.GetProperty("mediaLimits");
        Assert.Equal(10L * 1024 * 1024, limits.GetProperty("imageBytes").GetInt64());
        Assert.Equal(200L * 1024 * 1024, limits.GetProperty("videoBytes").GetInt64());
        Assert.Equal(300, limits.GetProperty("videoMaxSeconds").GetInt32());
        Assert.Equal(50L * 1024 * 1024, limits.GetProperty("audioBytes").GetInt64());

        var smtp = body.GetProperty("smtp");
        Assert.Null(smtp.GetProperty("host").GetString());
        Assert.Equal("null", smtp.GetProperty("port").GetRawText());
        Assert.False(smtp.GetProperty("hasPassword").GetBoolean());
    }
}

/// <summary>
/// PUT /admin/config 局部更新、校验失败不落库、邀请开关生效。
/// </summary>
public class AdminConfigUpdateTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public AdminConfigUpdateTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Partial_update_flow()
    {
        var admin = await _factory.EnsureAdminAsync();
        var client = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);

        // 局部更新：邀请关闭 + 图片上限 1KB + SMTP。
        var put = await client.PutAsJsonAsync("/admin/config", new
        {
            inviteEnabled = false,
            mediaLimits = new { imageBytes = 1024L },
            smtp = new
            {
                host = "smtp.example.com",
                port = 587,
                username = "mailer",
                password = "s3cret",
                fromAddress = "hzone@example.com",
            },
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("inviteEnabled").GetBoolean());
        Assert.Equal(1024L, body.GetProperty("mediaLimits").GetProperty("imageBytes").GetInt64());
        var smtp = body.GetProperty("smtp");
        Assert.Equal("smtp.example.com", smtp.GetProperty("host").GetString());
        Assert.Equal(587, smtp.GetProperty("port").GetInt32());
        Assert.Equal("mailer", smtp.GetProperty("username").GetString());
        Assert.Equal("hzone@example.com", smtp.GetProperty("fromAddress").GetString());
        Assert.True(smtp.GetProperty("hasPassword").GetBoolean());
        // password 不回显：响应只有 hasPassword。
        Assert.False(smtp.TryGetProperty("password", out _));

        // GET 持久化生效。
        var get = await client.GetFromJsonAsync<JsonElement>("/admin/config");
        Assert.False(get.GetProperty("inviteEnabled").GetBoolean());
        Assert.Equal(1024L, get.GetProperty("mediaLimits").GetProperty("imageBytes").GetInt64());
        Assert.True(get.GetProperty("smtp").GetProperty("hasPassword").GetBoolean());

        // 邀请关闭 → POST /invites 403 + 业务码。
        var blocked = await client.PostAsJsonAsync("/invites", new { });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("invites-disabled",
            (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // 重新打开 → 正常生成。
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/admin/config", new { inviteEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/invites", new { })).StatusCode);

        // 校验失败：数字非正 / 端口越界 → 400 且配置不变。
        var invalid1 = await client.PutAsJsonAsync("/admin/config", new
        {
            mediaLimits = new { imageBytes = 0L },
            smtp = new { port = 0 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid1.StatusCode);
        var invalid2 = await client.PutAsJsonAsync("/admin/config", new { smtp = new { port = 65536 } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid2.StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>("/admin/config");
        Assert.Equal(1024L, unchanged.GetProperty("mediaLimits").GetProperty("imageBytes").GetInt64());
        Assert.Equal(587, unchanged.GetProperty("smtp").GetProperty("port").GetInt32());
    }
}

/// <summary>
/// 未初始化实例：管理员端点 409。
/// </summary>
public class AdminConfigUninitializedTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public AdminConfigUninitializedTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_endpoints_return_409_before_setup()
    {
        // 未初始化也可直接建非管理员用户（直插 DB），用于过 [Authorize]。
        var user = await _factory.SeedUserAsync("early@test.local", "Early");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/admin/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync("/admin/config", new { })).StatusCode);
    }
}
