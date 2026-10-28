using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 点赞站内信：赞 → 通知作者（未读）；取消再赞 → 不重复；自己赞自己 → 无通知。
/// </summary>
public class LikeNotificationTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public LikeNotificationTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Liking_notifies_author_once_and_not_self()
    {
        var author = await _factory.SeedUserAsync("like-n1@test.local", "LikeN1");
        var fan = await _factory.SeedUserAsync("like-n2@test.local", "LikeN2");
        var authorClient = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var fanClient = _factory.CreateAuthorizedClient(fan.Id, fan.Nickname);

        // 定向分享要求好友关系：直插一条已接受好友
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            db.Friendships.Add(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = author.Id,
                AddresseeId = fan.Id,
                Status = FriendshipStatus.Accepted,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // 发给 fan（空收件人的私密随记对 fan 不可见，赞不了）
        var sayId = await PostSayAsync(authorClient, "被赞的随记", fan.Id);

        // 首赞 → 作者收到一条 liked 未读
        await fanClient.PostAsync($"/contents/{sayId}/like", null);
        var inbox1 = await authorClient.GetFromJsonAsync<JsonElement>("/inbox");
        Assert.Equal(1, inbox1.GetProperty("unreadCount").GetInt32());
        var msg = Assert.Single(inbox1.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "liked"));
        Assert.Equal(sayId, msg.GetProperty("contentId").GetGuid());

        // 取消 → 不删通知；再赞 → 去重不重复刷屏
        await fanClient.PostAsync($"/contents/{sayId}/like", null);
        await fanClient.PostAsync($"/contents/{sayId}/like", null);
        var inbox2 = await authorClient.GetFromJsonAsync<JsonElement>("/inbox");
        var likedCount = inbox2.GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("type").GetString() == "liked");
        Assert.Equal(1, likedCount);

        // 自己赞自己 → 无通知
        var selfSay = await PostSayAsync(authorClient, "自赞无通知");
        await authorClient.PostAsync($"/contents/{selfSay}/like", null);
        var inbox3 = await authorClient.GetFromJsonAsync<JsonElement>("/inbox");
        Assert.All(inbox3.GetProperty("items").EnumerateArray(),
            i => Assert.NotEqual(selfSay, i.GetProperty("contentId").GetGuid()));
    }

    private static async Task<Guid> PostSayAsync(HttpClient client, string text, Guid? recipient = null)
    {
        var res = await client.PostAsJsonAsync("/contents/says",
            new { text, recipientIds = recipient is null ? Array.Empty<Guid>() : new[] { recipient.Value } });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
