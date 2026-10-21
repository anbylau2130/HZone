using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 账号级偏好同步（PRD 6.9）：PUT /me/preferences（任意 JSON 对象，≤4KB）
/// + /auth/me 与 /auth/login 下发 preferences。
/// </summary>
public class PreferencesTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private const string Password = "Prefs!Passw0rd";

    private readonly HZoneApiFactory _factory;

    public PreferencesTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Put_preferences_reflects_in_me_and_relogin()
    {
        var client = await SeedLoginableClientAsync("prefs-a@test.local", "PrefsA");

        var payload = new { mode = "dark", presetId = "space", overrides = new { primary = "#112233" }, locale = "en-US" };
        var put = await client.PutAsJsonAsync("/me/preferences", payload);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var echoed = await ReadJson(put);
        Assert.Equal("dark", echoed.GetProperty("mode").GetString());

        // /auth/me 带账号偏好。
        var me = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal("dark", me.GetProperty("preferences").GetProperty("mode").GetString());
        Assert.Equal("en-US", me.GetProperty("preferences").GetProperty("locale").GetString());

        // 重新登录仍带（账号级，多端同步的数据源）。
        var login = await _factory.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "prefs-a@test.local", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await ReadJson(login);
        Assert.Equal("space",
            user.GetProperty("user").GetProperty("preferences").GetProperty("presetId").GetString());
    }

    [Fact]
    public async Task Me_without_preferences_returns_null()
    {
        var client = await SeedLoginableClientAsync("prefs-b@test.local", "PrefsB");

        var me = await ReadJson(await client.GetAsync("/auth/me"));
        Assert.Equal(JsonValueKind.Null, me.GetProperty("preferences").ValueKind);
    }

    [Fact]
    public async Task Oversized_preferences_returns_400()
    {
        var client = await SeedLoginableClientAsync("prefs-c@test.local", "PrefsC");

        var oversized = new Dictionary<string, object>();
        for (var i = 0; i < 200; i++)
        {
            oversized[$"key{i:000}"] = new string('x', 50);
        }

        Assert.True((await client.PutAsJsonAsync("/me/preferences", oversized))
            .StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_object_body_returns_400()
    {
        var client = await SeedLoginableClientAsync("prefs-d@test.local", "PrefsD");

        using var content = JsonContent.Create(new[] { 1, 2, 3 });
        var response = await client.PutAsync("/me/preferences", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_preferences_requires_authentication()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/me/preferences", new { mode = "dark" })).StatusCode);
    }

    /// <summary>SeedUserAsync 落的是占位 hash 且未验证邮箱（登录不了）；
    /// 偏好同步的 e2e 要真登录，这里建可登录账号（真 hash + EmailVerified）。</summary>
    private async Task<HttpClient> SeedLoginableClientAsync(string email, string nickname)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = hasher.Hash(Password),
            Nickname = nickname,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return _factory.CreateAuthorizedClient(user.Id, user.Nickname);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
