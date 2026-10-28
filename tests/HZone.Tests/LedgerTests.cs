using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 记账：个人账本惰性建/不可删、共同账本成员共记共阅、月度汇总口径、越权 404。
/// </summary>
public class LedgerTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public LedgerTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Personal_ledger_lifecycle_with_month_summary()
    {
        var user = await _factory.SeedUserAsync("led1@test.local", "Led1");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);

        // 惰性建个人账本；个人账本不可删（400）
        var ledgers = await client.GetFromJsonAsync<JsonElement>("/me/ledgers");
        var personal = Assert.Single(ledgers.GetProperty("items").EnumerateArray());
        Assert.Equal("我的账本", personal.GetProperty("name").GetString());
        Assert.False(personal.GetProperty("isShared").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.DeleteAsync($"/me/ledgers/{personal.GetProperty("id").GetGuid()}")).StatusCode);

        // 记两笔支出一笔收入（本月），一笔上月
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        var lastMonthKey = $"{lastMonth:yyyy-MM}";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries",
            Entry(1280, "expense", "food", "午饭", month))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries",
            Entry(4500, "expense", "transport", null, month))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries",
            Entry(88000, "income", "salary", "工资", month))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries",
            Entry(990, "expense", "food", "上月奶茶", lastMonthKey))).StatusCode);

        // 本月汇总：支出 57.80（1280+4500 分）、收入 880、分类占比 food > transport
        var res = await client.GetFromJsonAsync<JsonElement>(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries?month={month}");
        Assert.Equal(3, res.GetProperty("entries").GetArrayLength());
        var sum = res.GetProperty("summary");
        Assert.Equal(5780, sum.GetProperty("expenseCents").GetInt64());
        Assert.Equal(88000, sum.GetProperty("incomeCents").GetInt64());
        var byCat = sum.GetProperty("byCategory").EnumerateArray().ToList();
        Assert.Equal(2, byCat.Count);
        Assert.Equal("transport", byCat[0].GetProperty("category").GetString());
        Assert.Equal(4500, byCat[0].GetProperty("cents").GetInt64());
        Assert.Equal("food", byCat[1].GetProperty("category").GetString());

        // 上月只含上月那笔
        var res2 = await client.GetFromJsonAsync<JsonElement>(
            $"/me/ledgers/{personal.GetProperty("id").GetGuid()}/entries?month={lastMonthKey}");
        Assert.Single(res2.GetProperty("entries").EnumerateArray());

        // 非法负载：负金额/坏 kind/坏日期 → 400
        var ledgerId = personal.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/me/ledgers/{ledgerId}/entries", Entry(-1, "expense", "food", null, month))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/me/ledgers/{ledgerId}/entries", Entry(100, "transfer", "food", null, month))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/me/ledgers/{ledgerId}/entries", Entry(100, "expense", "food", null, "2026/10/01"))).StatusCode);
    }

    [Fact]
    public async Task Shared_ledger_members_and_access_control()
    {
        var owner = await _factory.SeedUserAsync("led2@test.local", "Led2");
        var friend = await _factory.SeedUserAsync("led3@test.local", "Led3");
        var stranger = await _factory.SeedUserAsync("led4@test.local", "Led4");
        var ownerClient = _factory.CreateAuthorizedClient(owner.Id, owner.Nickname);
        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        var strangerClient = _factory.CreateAuthorizedClient(stranger.Id, stranger.Nickname);

        // 好友关系（owner↔friend）
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.Friendships.Add(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = owner.Id,
                AddresseeId = friend.Id,
                Status = FriendshipStatus.Accepted,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // 建共同账本；非好友不可邀请；好友邀请成功
        var created = await ownerClient.PostAsJsonAsync("/me/ledgers", new { name = "我们的小账" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sharedId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.PostAsJsonAsync($"/me/ledgers/{sharedId}/members",
                new { userId = stranger.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ownerClient.PostAsJsonAsync($"/me/ledgers/{sharedId}/members",
                new { userId = friend.Id })).StatusCode);

        // 两个成员都能记；互相可见；记录人信息带上
        Assert.Equal(HttpStatusCode.Created, (await friendClient.PostAsJsonAsync(
            $"/me/ledgers/{sharedId}/entries", Entry(1990, "expense", "shopping", "TA 记的", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync(
            $"/me/ledgers/{sharedId}/entries", Entry(3000, "expense", "food", null, null))).StatusCode);
        var res = await friendClient.GetFromJsonAsync<JsonElement>($"/me/ledgers/{sharedId}/entries");
        Assert.Equal(2, res.GetProperty("entries").GetArrayLength());
        var names = res.GetProperty("entries").EnumerateArray()
            .Select(x => x.GetProperty("recorder").GetProperty("nickname").GetString()).ToHashSet();
        Assert.Contains("Led2", names);
        Assert.Contains("Led3", names);

        // 陌生人不可见/不可记（404）；条目改删仅记录人（owner 改 friend 记的 → 404）
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.GetAsync($"/me/ledgers/{sharedId}/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.PostAsJsonAsync(
                $"/me/ledgers/{sharedId}/entries", Entry(100, "expense", "food", null, null))).StatusCode);
        var friendEntry = res.GetProperty("entries").EnumerateArray()
            .First(x => x.GetProperty("recorder").GetProperty("nickname").GetString() == "Led3");
        var friendEntryId = friendEntry.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerClient.PutAsJsonAsync($"/ledger/entries/{friendEntryId}",
                Entry(1, "expense", "food", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await friendClient.DeleteAsync($"/ledger/entries/{friendEntryId}")).StatusCode);

        // 删除共同账本：非创建人 403；创建人 OK 且条目随删
        Assert.Equal(HttpStatusCode.Forbidden,
            (await friendClient.DeleteAsync($"/me/ledgers/{sharedId}")).StatusCode);
        await ownerClient.DeleteAsync($"/me/ledgers/{sharedId}");
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerClient.GetAsync($"/me/ledgers/{sharedId}/entries")).StatusCode);
    }

    private static object Entry(long cents, string kind, string category, string? note, string? month) =>
        new
        {
            amountCents = cents,
            kind,
            category,
            note,
            entryDate = month is null
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : $"{month}-15"
        };
}
