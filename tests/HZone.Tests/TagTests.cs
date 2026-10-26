using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

/// <summary>
/// 内容标签：创建/更新整体替换、规范化（trim/去重/上限）、响应携带、
/// /me/contents 与 /search 的 tag 筛选、/me/tags 标签云计数。
/// </summary>
public class TagTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public TagTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_with_tags_normalizes_and_returns_them()
    {
        var user = await _factory.SeedUserAsync("tag1a@test.local", "Tag1a");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // trim + 空串剔除 + 大小写去重（保序）
        var created = await client.PostAsJsonAsync("/contents/journals", new
        {
            title = "标签笔记",
            markdown = "正文",
            recipientIds = Array.Empty<Guid>(),
            tags = new[] { " 旅行 ", "", "旅行", "爸爸" },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var tags = body.GetProperty("tags");
        Assert.Equal(2, tags.GetArrayLength());
        Assert.Equal("旅行", tags[0].GetString());
        Assert.Equal("爸爸", tags[1].GetString());

        // 详情/列表响应同形携带
        var detail = await client.GetFromJsonAsync<JsonElement>($"/contents/{body.GetProperty("id").GetGuid()}");
        Assert.Equal("旅行", detail.GetProperty("tags")[0].GetString());
        var list = await client.GetFromJsonAsync<JsonElement>("/me/contents");
        Assert.Equal("爸爸", list.GetProperty("items")[0].GetProperty("tags")[1].GetString());
    }

    [Fact]
    public async Task Update_replaces_tags_and_empty_clears()
    {
        var user = await _factory.SeedUserAsync("tag1b@test.local", "Tag1b");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var created = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "带标签随记",
            recipientIds = Array.Empty<Guid>(),
            tags = new[] { "旧标签", "保留" },
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 整体替换（tags=null 不动；传新数组全量替换）
        var updated = await client.PutAsJsonAsync($"/contents/{id}", new { tags = new[] { "保留", "新标签" } });
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, updatedBody.GetProperty("tags").GetArrayLength());
        Assert.DoesNotContain("旧标签", updatedBody.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));

        // 空数组 = 清空全部
        var cleared = await client.PutAsJsonAsync($"/contents/{id}", new { tags = Array.Empty<string>() });
        var clearedBody = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, clearedBody.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Tag_validation_limits()
    {
        var user = await _factory.SeedUserAsync("tag1c@test.local", "Tag1c");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var tooMany = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "x",
            recipientIds = Array.Empty<Guid>(),
            tags = Enumerable.Range(1, 11).Select(i => $"t{i}").ToArray(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var tooLong = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "x",
            recipientIds = Array.Empty<Guid>(),
            tags = new[] { new string('长', 33) },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        // 边界值合法：10 个、32 字符
        var edge = await client.PostAsJsonAsync("/contents/says", new
        {
            text = "x",
            recipientIds = Array.Empty<Guid>(),
            tags = Enumerable.Range(1, 10).Select(i => new string('标', 3) + i).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, edge.StatusCode);
    }

    [Fact]
    public async Task Filter_by_tag_and_tag_cloud_counts()
    {
        var user = await _factory.SeedUserAsync("tag1d@test.local", "Tag1d");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        var other = await _factory.SeedUserAsync("tag1e@test.local", "Tag1e");
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);

        await client.PostAsJsonAsync("/contents/journals", new
        {
            title = "京都行", markdown = "a", recipientIds = Array.Empty<Guid>(), tags = new[] { "旅行" },
        });
        await client.PostAsJsonAsync("/contents/journals", new
        {
            title = "爸爸的菜谱", markdown = "b", recipientIds = Array.Empty<Guid>(), tags = new[] { "爸爸", "旅行" },
        });
        // 他人内容不进我的标签云/筛选
        await otherClient.PostAsJsonAsync("/contents/journals", new
        {
            title = "别人的", markdown = "c", recipientIds = Array.Empty<Guid>(), tags = new[] { "旅行" },
        });

        // /me/contents?tag= 旅行 → 2 条
        var byTag = await client.GetFromJsonAsync<JsonElement>(
            "/me/contents?tag=" + Uri.EscapeDataString("旅行"));
        Assert.Equal(2, byTag.GetProperty("total").GetInt32());

        // /search?tag= 与关键词叠加 → 1 条
        var searchTag = await client.GetFromJsonAsync<JsonElement>(
            "/search?q=" + Uri.EscapeDataString("菜谱") + "&tag=" + Uri.EscapeDataString("爸爸"));
        Assert.Equal(1, searchTag.GetProperty("total").GetInt32());
        Assert.Equal("爸爸的菜谱", searchTag.GetProperty("items")[0].GetProperty("title").GetString());

        // /me/tags 标签云：旅行 2、爸爸 1（按计数降序）
        var cloud = await client.GetFromJsonAsync<JsonElement>("/me/tags");
        var items = cloud.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("旅行", items[0].GetProperty("tag").GetString());
        Assert.Equal(2, items[0].GetProperty("count").GetInt32());
        Assert.Equal("爸爸", items[1].GetProperty("tag").GetString());
        Assert.Equal(1, items[1].GetProperty("count").GetInt32());

        // 内容删除后标签级联消失、标签云计数回撤
        var first = await client.GetFromJsonAsync<JsonElement>("/me/contents");
        var delId = first.GetProperty("items")[0].GetProperty("id").GetGuid();
        await client.DeleteAsync($"/contents/{delId}");
        var cloudAfter = await client.GetFromJsonAsync<JsonElement>("/me/tags");
        var travel = cloudAfter.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("tag").GetString() == "旅行");
        Assert.Equal(1, travel.GetProperty("count").GetInt32());
    }
}
