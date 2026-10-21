using System.Security.Claims;
using System.Text;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using HZone.Api;
using HZone.Api.Authentication;
using HZone.Api.Endpoints;
using HZone.Api.Services;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration["HZone:Database:ConnectionString"] ?? "Data Source=data/hzone.db";
var jwtKey =
    builder.Configuration["HZone:Auth:JwtKey"] ?? "dev-only-insecure-jwt-key-0246802468";
var jwtIssuer = builder.Configuration["HZone:Auth:Issuer"] ?? "HZone";
var jwtAudience = builder.Configuration["HZone:Auth:Audience"] ?? "HZone";

builder.Services.AddDbContext<HZoneDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IContentQuery, ContentQuery>();
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<IEmailSender, FileEmailSender>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IFFmpegCompressor, FFmpegCompressor>();

builder.Services
    .AddAuthentication(options => options.DefaultScheme = "HZone.Auth")
    .AddPolicyScheme("HZone.Auth", "HZone.Auth", options =>
    {
        // Bearer 以 hz_ 开头 → PAT 认证，其余（含无头）→ JWT Bearer（NoResult → 401）。
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorization["Bearer ".Length..].Trim()
                : null;
            return token is not null && token.StartsWith(PatToken.Prefix, StringComparison.Ordinal)
                ? PatAuthHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
        // 禁用用户即使持有效 JWT 也拒绝（查库实时校验），禁用立即生效。
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<HZoneDbContext>();
                var sub = context.Principal?.FindFirstValue("sub");
                var user = Guid.TryParse(sub, out var userId)
                    ? await db.Users.AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.IsDisabled })
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted)
                    : null;
                if (user is null)
                {
                    context.Fail("user missing");
                }
                else if (user.IsDisabled)
                {
                    context.Fail("user disabled");
                }
            },
        };
    })
    .AddScheme<PatAuthenticationOptions, PatAuthHandler>(PatAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

DatabaseBootstrapper.EnsureDatabase(app, connectionString);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapContentEndpoints();
app.MapMeStatsEndpoints();
app.MapCommentEndpoints();
app.MapInboxEndpoints();
app.MapMediaEndpoints();
app.MapMeAvatarEndpoints();
app.MapMeProfileEndpoints();
app.MapExportImportEndpoints();
app.MapFriendEndpoints();
app.MapSetupEndpoints();
app.MapAdminEndpoints();
app.MapInviteEndpoints();
app.MapAuthEndpoints();
app.MapPatEndpoints();

app.Run();

public partial class Program { }
