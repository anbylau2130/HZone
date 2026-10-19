using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Api.Authentication;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// /admin/users：分页列表、禁用/启用、禁用后登录与 JWT/PAT 即时失效。
/// </summary>
public class AdminUserTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public AdminUserTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_lists_users_with_pagination()
    {
        var admin = await _factory.EnsureAdminAsync();
        var u1 = await _factory.SeedUserAsync("list-a@test.local", "ListA");
        var u2 = await _factory.SeedUserAsync("list-b@test.local", "ListB");
        var client = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);

        var page1 = await client.GetFromJsonAsync<JsonElement>("/admin/users?offset=0&limit=2");
        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        var first = page1.GetProperty("items")[0];
        Assert.Equal(admin.Id, first.GetProperty("id").GetGuid());
        Assert.True(first.GetProperty("isAdmin").GetBoolean());
        Assert.False(first.GetProperty("isDisabled").GetBoolean());

        var page2 = await client.GetFromJsonAsync<JsonElement>("/admin/users?offset=2&limit=2");
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        // u1/u2 可能同毫秒创建，CreatedAt 并列时按 Id 排序不可预知，这里只断言集合覆盖。
        var ids = page1.GetProperty("items").EnumerateArray()
            .Concat(page2.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(u1.Id, ids);
        Assert.Contains(u2.Id, ids);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task Disable_self_returns_400()
    {
        var admin = await _factory.EnsureAdminAsync();
        var client = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var response = await client.PutAsJsonAsync($"/admin/users/{admin.Id}/disable",
            new { isDisabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disable_unknown_user_returns_404()
    {
        var admin = await _factory.EnsureAdminAsync();
        var client = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);
        var response = await client.PutAsJsonAsync($"/admin/users/{Guid.NewGuid()}/disable",
            new { isDisabled = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_user_blocked_on_login_jwt_and_pat()
    {
        var admin = await _factory.EnsureAdminAsync();
        var adminClient = _factory.CreateAuthorizedClient(admin.Id, admin.Nickname);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = "victim@test.local",
                PasswordHash = hasher.Hash("password123"),
                Nickname = "Victim",
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
            });
            db.PersonalAccessTokens.Add(new PersonalAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = db.Users.Local.Single(u => u.Email == "victim@test.local").Id,
                Name = "test",
                TokenHash = PatToken.Sha256Hex("hz_victimtoken0123456789ab"),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var victimClient = await LoginAsync("victim@test.local", "password123");
        Assert.Equal(HttpStatusCode.OK,
            (await victimClient.GetAsync("/auth/me")).StatusCode);
        var patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "hz_victimtoken0123456789ab");
        Assert.Equal(HttpStatusCode.OK, (await patClient.GetAsync("/auth/me")).StatusCode);

        var victimId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            victimId = await db.Users.Where(u => u.Email == "victim@test.local")
                .Select(u => u.Id).SingleAsync();
        }

        // 禁用。
        var disable = await adminClient.PutAsJsonAsync($"/admin/users/{victimId}/disable",
            new { isDisabled = true });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        // 登录 401（登录本就拒绝禁用用户）。
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "victim@test.local", password = "password123" })).StatusCode);

        // 有效 JWT 立即失效 → 401（JwtBearer OnTokenValidated 查库）。
        var jwtClient = _factory.CreateAuthorizedClient(victimId, "Victim");
        Assert.Equal(HttpStatusCode.Unauthorized, (await jwtClient.GetAsync("/auth/me")).StatusCode);

        // PAT 同样被拒。
        Assert.Equal(HttpStatusCode.Unauthorized, (await patClient.GetAsync("/auth/me")).StatusCode);

        // 重新启用 → 恢复。
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync(
            $"/admin/users/{victimId}/disable", new { isDisabled = false })).StatusCode);
        var revived = await LoginAsync("victim@test.local", "password123");
        Assert.Equal(HttpStatusCode.OK, (await revived.GetAsync("/auth/me")).StatusCode);
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/login",
            new { email, password });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
