using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Api;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 独立 factory（干净库）验证初始化向导全流程。类内单个 [Fact] 保证步骤顺序可控。
/// </summary>
public class SetupWizardFlowTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public SetupWizardFlowTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Wizard_flow_initializes_instance_and_unblocks_contents()
    {
        var client = _factory.CreateClient();

        // 1. 未初始化：status → false
        var statusBefore = await client.GetFromJsonAsync<JsonElement>("/setup/status");
        Assert.False(statusBefore.GetProperty("initialized").GetBoolean());

        // 2. 未初始化：带合法 token 的 contents POST → 409
        var author = await _factory.SeedUserAsync("flow-author@test.local", "FlowAuthor");
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var early = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "too early", recipientIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        // 3. 创建管理员 → 201
        var create = await client.PostAsJsonAsync("/setup/admin",
            new { email = "FlowAdmin@Test.Local", password = "correct horse battery", nickname = "FlowAdmin" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("flowadmin@test.local", created.GetProperty("email").GetString());
        Assert.Equal("FlowAdmin", created.GetProperty("nickname").GetString());
        Assert.NotEqual(Guid.Empty, created.GetProperty("id").GetGuid());

        // 4. status → true
        var statusAfter = await client.GetFromJsonAsync<JsonElement>("/setup/status");
        Assert.True(statusAfter.GetProperty("initialized").GetBoolean());

        // 5. 二次 POST → 409
        var second = await client.PostAsJsonAsync("/setup/admin",
            new { email = "second@test.local", password = "another password", nickname = "Second" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // 6. 初始化后 contents 恢复正常 → 201
        var ok = await authorClient.PostAsJsonAsync("/contents/says",
            new { text = "after init", recipientIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        // 7. SystemConfig 标记落地
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        Assert.True(await db.SystemConfigs.AnyAsync(c => c.Key == SetupState.SetupCompletedKey));
    }
}

/// <summary>
/// 校验失败不改变初始化状态；独立 factory 保证互不干扰。
/// xUnit 类内按方法名字母序执行，400 用例（B/B/S 开头）先于 Status 用例运行。
/// </summary>
public class SetupAdminValidationTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public SetupAdminValidationTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Bad_email_returns_400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/setup/admin",
            new { email = "not-an-email", password = "long-enough-pass", nickname = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Blank_nickname_returns_400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/setup/admin",
            new { email = "nick@test.local", password = "long-enough-pass", nickname = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Short_password_returns_400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/setup/admin",
            new { email = "short@test.local", password = "short", nickname = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_remains_false_after_failed_validation_attempts()
    {
        var body = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/setup/status");
        Assert.False(body.GetProperty("initialized").GetBoolean());
    }
}
