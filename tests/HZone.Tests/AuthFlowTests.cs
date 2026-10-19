using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

public class AuthFlowTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public AuthFlowTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_flow_register_verify_login_me_forgot_reset()
    {
        var client = _factory.CreateClient();
        await _factory.SeedSetupAsync();

        // 1. 创建邀请码 → 201 { code, expiresAt }
        var code = await CreateInviteAsync("inviter-flow@test.local");
        Assert.True(code.Length >= 8);

        // 2. 注册 → 201 pending（邮箱小写归一）
        var register = await client.PostAsJsonAsync("/auth/register",
            new { inviteCode = code, email = "Alice@Test.Local", password = "correct horse", nickname = "Alice" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice@test.local", registered.GetProperty("email").GetString());
        Assert.False(registered.GetProperty("emailVerified").GetBoolean());
        Assert.NotEqual(Guid.Empty, registered.GetProperty("userId").GetGuid());

        // 邀请码注册即占用
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var usedInvite = await db.InviteCodes.SingleAsync(i => i.Code == code);
            Assert.NotNull(usedInvite.UsedAt);
        }

        // 3. 未验证账号登录 → 401
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login",
            new { email = "alice@test.local", password = "correct horse" })).StatusCode);

        // 4. .eml 提取 token → 验证 → 200
        var verifyToken = await EmlHelper.ExtractLatestTokenAsync(_factory.DataDirectory, "verify-email");
        var verify = await client.PostAsJsonAsync("/auth/verify-email", new { token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        // 5. token 一次性：复用 → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/verify-email",
            new { token = verifyToken })).StatusCode);

        // 6. 登录 → 200 { token, expiresAt, user }
        var login = await client.PostAsJsonAsync("/auth/login",
            new { email = "alice@test.local", password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(loginBody.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
        Assert.Equal("Alice", loginBody.GetProperty("user").GetProperty("nickname").GetString());
        Assert.False(loginBody.GetProperty("user").GetProperty("isAdmin").GetBoolean());

        // 7. GET /auth/me
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice@test.local", meBody.GetProperty("email").GetString());
        Assert.False(meBody.GetProperty("isAdmin").GetBoolean());

        // 8. 忘记密码 → 恒 200 + .eml
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/forgot-password",
            new { email = "alice@test.local" })).StatusCode);
        var resetToken = await EmlHelper.ExtractLatestTokenAsync(_factory.DataDirectory, "reset-password");

        // 9. 重置密码 → 200
        var reset = await client.PostAsJsonAsync("/auth/reset-password",
            new { token = resetToken, newPassword = "new password 123" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // 10. 新密码登录成功 / 旧密码 401
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/login",
            new { email = "alice@test.local", password = "new password 123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login",
            new { email = "alice@test.local", password = "correct horse" })).StatusCode);
    }

    [Fact]
    public async Task Register_with_wrong_invite_code_returns_400()
    {
        await _factory.SeedSetupAsync();
        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/register",
            new { inviteCode = "NOPE0000", email = "wrong-invite@test.local", password = "long-enough", nickname = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("inviteCode", out _));
    }

    [Fact]
    public async Task Register_with_expired_invite_code_returns_400()
    {
        await _factory.SeedSetupAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.InviteCodes.Add(new InviteCode
        {
            Id = Guid.NewGuid(),
            Code = "EXPIRED1",
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/register",
            new { inviteCode = "EXPIRED1", email = "expired-invite@test.local", password = "long-enough", nickname = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_duplicate_email_returns_409()
    {
        await _factory.SeedSetupAsync();
        var client = _factory.CreateClient();

        var code1 = await CreateInviteAsync("inviter-dup1@test.local");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/auth/register",
            new { inviteCode = code1, email = "dup@test.local", password = "password123", nickname = "Dup" })).StatusCode);

        var code2 = await CreateInviteAsync("inviter-dup2@test.local");
        var second = await client.PostAsJsonAsync("/auth/register",
            new { inviteCode = code2, email = "dup@test.local", password = "password123", nickname = "Dup" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Fourth_active_invite_returns_409()
    {
        await _factory.SeedSetupAsync();
        var user = await _factory.SeedUserAsync("quota@test.local", "Quota");
        var client = _factory.CreateAuthorizedClient(user.Id, user.Nickname);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/invites", new { })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/invites", new { })).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_returns_ok()
    {
        await _factory.SeedSetupAsync();
        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/forgot-password",
            new { email = "nobody@test.local" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_account_cannot_login()
    {
        await _factory.SeedSetupAsync();
        var code = await CreateInviteAsync("inviter-bob@test.local");
        var register = await _factory.CreateClient().PostAsJsonAsync("/auth/register",
            new { inviteCode = code, email = "bob@test.local", password = "password123", nickname = "Bob" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "bob@test.local", password = "password123" })).StatusCode);
    }

    [Fact]
    public async Task Invites_require_authorization()
    {
        await _factory.SeedSetupAsync();
        var response = await _factory.CreateClient().PostAsJsonAsync("/invites", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> CreateInviteAsync(string inviterEmail)
    {
        var inviter = await _factory.SeedUserAsync(inviterEmail, "Inviter");
        var client = _factory.CreateAuthorizedClient(inviter.Id, inviter.Nickname);
        var response = await client.PostAsJsonAsync("/invites", new { });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString()!;
    }
}
