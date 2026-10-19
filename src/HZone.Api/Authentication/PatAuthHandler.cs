using System.Security.Claims;
using System.Text.Encodings.Web;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HZone.Api.Authentication;

public sealed class PatAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// PAT（hz_ 前缀 Bearer）认证：SHA-256 哈希查表、未吊销 → 构造与 JWT 同口径身份（sub=UserId, nickname）。
/// jwt vs pat 的 scheme 选择由 Program.cs 的 PolicyScheme 按前缀分流。
/// </summary>
public sealed class PatAuthHandler : AuthenticationHandler<PatAuthenticationOptions>
{
    public const string SchemeName = "Pat";

    public PatAuthHandler(
        IOptionsMonitor<PatAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
        if (string.IsNullOrEmpty(token) || !token.StartsWith(PatToken.Prefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var db = Request.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
        var pat = await db.PersonalAccessTokens.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenHash == PatToken.Sha256Hex(token));
        if (pat is null)
        {
            return AuthenticateResult.Fail("invalid PAT");
        }

        if (pat.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("PAT revoked");
        }

        var owner = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == pat.UserId);
        if (owner is null)
        {
            return AuthenticateResult.Fail("PAT owner missing");
        }

        if (owner.IsDisabled)
        {
            return AuthenticateResult.Fail("PAT owner disabled");
        }

        // 与 JWT 签发口径一致：sub=UserId、nickname；端点统一用 sub 取当前用户。
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", pat.UserId.ToString()),
            new Claim("nickname", owner.Nickname),
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
