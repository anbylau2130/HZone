using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

public class FriendTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    private User _admin = null!;

    public FriendTests(HZoneApiFactory fixture) => _factory = fixture;

    public async Task InitializeAsync()
    {
        _admin = await _factory.SeedSetupAsync() ?? new User { Id = Guid.Empty };
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Request_accept_then_both_see_each_other_outsider_does_not()
    {
        var a = await _factory.SeedUserAsync("fa1@test.local", "FA1");
        var b = await _factory.SeedUserAsync("fb1@test.local", "FB1");
        var c = await _factory.SeedUserAsync("fc1@test.local", "FC1");
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);
        var cClient = _factory.CreateAuthorizedClient(c.Id, c.Nickname);

        var create = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        var requestId = created.GetProperty("id").GetGuid();

        var accept = await bClient.PostAsync($"/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var aFriends = await ReadJson(await aClient.GetAsync("/friends"));
        var bFriends = await ReadJson(await bClient.GetAsync("/friends"));
        Assert.Equal(b.Id, aFriends[0].GetProperty("id").GetGuid());
        Assert.Equal("FB1", aFriends[0].GetProperty("nickname").GetString());
        Assert.Equal(a.Id, bFriends[0].GetProperty("id").GetGuid());
        Assert.Equal("FA1", bFriends[0].GetProperty("nickname").GetString());

        var cFriends = await ReadJson(await cClient.GetAsync("/friends"));
        Assert.Equal(0, cFriends.GetArrayLength());
    }

    [Fact]
    public async Task Duplicate_request_conflict_self_request_bad_request()
    {
        var a = await _factory.SeedUserAsync("fa2@test.local", "FA2");
        var b = await _factory.SeedUserAsync("fb2@test.local", "FB2");
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);

        var create = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var duplicate = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var reverse = await bClient.PostAsJsonAsync("/friends/requests", new { targetUserId = a.Id });
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);

        var self = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = a.Id });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task Accept_only_by_addressee_requester_gets_404()
    {
        var a = await _factory.SeedUserAsync("fa3@test.local", "FA3");
        var b = await _factory.SeedUserAsync("fb3@test.local", "FB3");
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);

        var create = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        var requestId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var byRequester = await aClient.PostAsync($"/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, byRequester.StatusCode);

        var declineByRequester = await aClient.PostAsync($"/friends/requests/{requestId}/decline", null);
        Assert.Equal(HttpStatusCode.NotFound, declineByRequester.StatusCode);

        var byAddressee = await bClient.PostAsync($"/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, byAddressee.StatusCode);
    }

    [Fact]
    public async Task Decline_deletes_relation_row()
    {
        var a = await _factory.SeedUserAsync("fa4@test.local", "FA4");
        var b = await _factory.SeedUserAsync("fb4@test.local", "FB4");
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);

        var create = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        var requestId = (await ReadJson(create)).GetProperty("id").GetGuid();

        var decline = await bClient.PostAsync($"/friends/requests/{requestId}/decline", null);
        Assert.Equal(HttpStatusCode.OK, decline.StatusCode);
    }

    [Fact]
    public async Task Delete_friend_bilateral_unfriend()
    {
        var a = await _factory.SeedUserAsync("fa5@test.local", "FA5");
        var b = await _factory.SeedUserAsync("fb5@test.local", "FB5");
        await _factory.SeedFriendshipAsync(a, b);
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);
        var bClient = _factory.CreateAuthorizedClient(b.Id, b.Nickname);

        var before = await ReadJson(await aClient.GetAsync("/friends"));
        Assert.Equal(1, before.GetArrayLength());

        var remove = await aClient.DeleteAsync($"/friends/{b.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var afterA = await ReadJson(await aClient.GetAsync("/friends"));
        var afterB = await ReadJson(await bClient.GetAsync("/friends"));
        Assert.Equal(0, afterA.GetArrayLength());
        Assert.Equal(0, afterB.GetArrayLength());
    }

    [Fact]
    public async Task Friend_request_writes_inbox_message()
    {
        var a = await _factory.SeedUserAsync("fa6@test.local", "FA6");
        var b = await _factory.SeedUserAsync("fb6@test.local", "FB6");
        var aClient = _factory.CreateAuthorizedClient(a.Id, a.Nickname);

        var create = await aClient.PostAsJsonAsync("/friends/requests", new { targetUserId = b.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        var message = await db.InboxMessages.SingleOrDefaultAsync(
            m => m.UserId == b.Id && m.Type == InboxMessageType.FriendRequest);
        Assert.NotNull(message);
        Assert.Null(message.ContentId);
    }

    [Fact]
    public async Task Friend_endpoints_require_initialized_instance()
    {
        using var factory = new HZoneApiFactory();
        var user = await factory.SeedUserAsync("fa7@test.local", "FA7");
        var client = factory.CreateAuthorizedClient(user.Id, user.Nickname);

        var send = await client.PostAsJsonAsync("/friends/requests", new { targetUserId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);

        var list = await client.GetAsync("/friends");
        Assert.Equal(HttpStatusCode.Conflict, list.StatusCode);
    }
}
