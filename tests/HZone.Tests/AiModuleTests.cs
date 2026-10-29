using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Api.Services;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// AI 模块 CRUD：Provider（key 只写不回显）/ 技能 / 外部 MCP server / 会话与消息。
/// 对外 MCP Server（/mcp JSON-RPC）与内置工具执行器的行为。
/// </summary>
public class AiModuleTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public AiModuleTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Provider_skills_mcp_crud_and_masking()
    {
        var user = await _factory.SeedUserAsync("ai1@test.local", "Ai1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // Provider：默认空 → 写入（key 不回显，只带 hasKey）→ 更新 model 不覆盖 key
        var empty = await client.GetFromJsonAsync<JsonElement>("/me/ai/provider");
        Assert.False(empty.GetProperty("hasKey").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/me/ai/provider",
            new { baseUrl = "https://api.example.com/v4", apiKey = "sk-secret", model = "glm-4.7" })).StatusCode);
        var saved = await client.GetFromJsonAsync<JsonElement>("/me/ai/provider");
        Assert.Equal("https://api.example.com/v4", saved.GetProperty("baseUrl").GetString());
        Assert.Equal("glm-4.7", saved.GetProperty("model").GetString());
        Assert.True(saved.GetProperty("hasKey").GetBoolean());
        Assert.False(saved.TryGetProperty("apiKey", out _));
        // 只改 model（不传 apiKey）→ key 保留
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/me/ai/provider",
            new { baseUrl = "https://api.example.com/v4", model = "glm-4.5" })).StatusCode);
        var saved2 = await client.GetFromJsonAsync<JsonElement>("/me/ai/provider");
        Assert.True(saved2.GetProperty("hasKey").GetBoolean());
        // 非法 baseUrl → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/me/ai/provider",
            new { baseUrl = "ftp://x", model = "m" })).StatusCode);

        // 技能 CRUD
        var skill = await client.PostAsJsonAsync("/me/ai/skills",
            new { name = "翻译官", icon = "🌐", prompt = "你是一名中英互译助手，逐句翻译。" });
        Assert.Equal(HttpStatusCode.Created, skill.StatusCode);
        var skillId = (await skill.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/me/ai/skills/{skillId}",
            new { name = "翻译家", icon = "🌐", prompt = "升级版" })).StatusCode);
        var skills = await client.GetFromJsonAsync<JsonElement>("/me/ai/skills");
        Assert.Equal("翻译家", Assert.Single(skills.GetProperty("items").EnumerateArray())
            .GetProperty("name").GetString());
        // 空 prompt → 400；他人技能 → 404
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/me/ai/skills",
            new { name = "x", prompt = "" })).StatusCode);

        // MCP server CRUD + URL 校验
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/me/ai/mcp-servers",
            new { name = "x", url = "notaurl" })).StatusCode);
        var server = await client.PostAsJsonAsync("/me/ai/mcp-servers",
            new { name = "天气服务", url = "https://mcp.example.com/mcp", authHeader = "Authorization: Bearer t" });
        Assert.Equal(HttpStatusCode.Created, server.StatusCode);
        var serverBody = await server.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(serverBody.GetProperty("hasAuth").GetBoolean());
        var servers = await client.GetFromJsonAsync<JsonElement>("/me/ai/mcp-servers");
        Assert.Single(servers.GetProperty("items").EnumerateArray());

        // 会话 CRUD + 消息历史
        var conv = await client.PostAsJsonAsync("/me/ai/conversations", new { });
        var convId = (await conv.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var list = await client.GetFromJsonAsync<JsonElement>("/me/ai/conversations");
        Assert.Single(list.GetProperty("items").EnumerateArray());

        // chat 未配置完整 provider（apiKey 有但 baseUrl 换成 example 会真实外呼——这里测校验路径：
        // 他人会话 404 / 内容为空 400）
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/me/ai/conversations/{Guid.NewGuid()}/chat", new { content = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/me/ai/conversations/{convId}/chat", new { content = "" })).StatusCode);

        // 他人资源统一 404
        var other = await _factory.SeedUserAsync("ai2@test.local", "Ai2");
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/me/ai/skills/{skillId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(
            $"/me/ai/conversations/{convId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/me/ai/conversations/{convId}")).StatusCode);
    }

    [Fact]
    public async Task Mcp_host_jsonrpc_lifecycle_with_pat()
    {
        var user = await _factory.SeedUserAsync("ai3@test.local", "Ai3");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 种子：一条含关键词的随记 + 一篇笔记（直接插库控制内容）
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.Contents.Add(new Say
            {
                Id = Guid.NewGuid(), AuthorId = user.Id, Text = "MCP 测试用的晚霞随记",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // JSON-RPC：initialize → tools/list → tools/call（搜索）
        var init = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2024-11-05" } });
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        var initBody = await init.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HZone", initBody.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        var list = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        var toolNames = listBody.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("hz_search_content", toolNames);
        Assert.Contains("hz_create_journal", toolNames);

        var call = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 3, method = "tools/call",
            @params = new { name = "hz_search_content", arguments = new { q = "晚霞" } },
        });
        var callBody = await call.Content.ReadFromJsonAsync<JsonElement>();
        var text = callBody.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.True(text.Contains("晚霞"), $"search text: {text}");

        // 未知方法 → -32601
        var bad = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 4, method = "nope" });
        var badBody = await bad.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(-32601, badBody.GetProperty("error").GetProperty("code").GetInt32());

        // 未认证 → 401
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/mcp",
                new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } })).StatusCode);
    }

    [Fact]
    public async Task Ai_builtin_tools_execute_read_and_write()
    {
        var user = await _factory.SeedUserAsync("ai4@test.local", "Ai4");

        // create_journal（写）
        var createResult = await AiTools.ExecuteAsync(Db(), user.Id, "create_journal",
            JsonDocument.Parse("""{"title":"AI 建的笔记","markdown":"# 由工具创建"}""").RootElement, default);
        var created = JsonDocument.Parse(createResult).RootElement;
        Assert.True(created.TryGetProperty("ok", out var okVal) && okVal.GetBoolean(),
            $"create_journal failed: {createResult}");
        var journalId = created.GetProperty("id").GetGuid();

        // read_journal（读，含可见性）
        var readRaw = await AiTools.ExecuteAsync(Db(), user.Id, "read_journal",
            JsonDocument.Parse($"{{\"id\":\"{journalId}\"}}").RootElement, default);
        var read = JsonDocument.Parse(readRaw).RootElement;
        Assert.True(read.GetProperty("title").GetString() == "AI 建的笔记", readRaw);

        // add_ledger_entry（写）→ get_ledger_summary（读）
        var entry = JsonDocument.Parse(await AiTools.ExecuteAsync(Db(), user.Id, "add_ledger_entry",
            JsonDocument.Parse("""{"amount":12.5,"kind":"expense","category":"food","note":"AI 记的午饭"}""").RootElement, default)).RootElement;
        Assert.True(entry.GetProperty("ok").GetBoolean(), "entry-step");
        var summary = JsonDocument.Parse(await AiTools.ExecuteAsync(Db(), user.Id, "get_ledger_summary",
            JsonDocument.Parse("{}").RootElement, default)).RootElement;
        Assert.True(summary.GetProperty("expenseCents").GetInt64() == 1250, "summary-step");

        // search_content（读）
        var search = JsonDocument.Parse(await AiTools.ExecuteAsync(Db(), user.Id, "search_content",
            JsonDocument.Parse("""{"q":"由工具创建"}""").RootElement, default)).RootElement;
        Assert.True(search.GetProperty("journals").GetArrayLength() != 0, "search-step");

        // get_stats
        var stats = JsonDocument.Parse(await AiTools.ExecuteAsync(Db(), user.Id, "get_stats",
            JsonDocument.Parse("{}").RootElement, default)).RootElement;
        Assert.True(stats.GetProperty("journals").GetInt32() == 1, "stats-step");

        // 未知工具 → error JSON（不 throw）
        var unknown = await AiTools.ExecuteAsync(Db(), user.Id, "nope",
            JsonDocument.Parse("{}").RootElement, default);
        Assert.Contains("unknown tool", unknown);
    }

    private HZoneDbContext Db()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
    }
}
