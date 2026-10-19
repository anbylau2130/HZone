using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net.Http.Headers;
using System.Text;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HZone.Tests.Helpers;

public static class TestAuth
{
    public static string MintToken(this HZoneApiFactory factory, Guid userId, string nickname)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("nickname", nickname),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(HZoneApiFactory.JwtKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: HZoneApiFactory.JwtIssuer,
            audience: HZoneApiFactory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static System.Net.Http.HttpClient CreateAuthorizedClient(
        this HZoneApiFactory factory, Guid userId, string nickname)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.MintToken(userId, nickname));
        return client;
    }
}
