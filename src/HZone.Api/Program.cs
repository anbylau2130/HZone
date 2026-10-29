using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
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
var jwtKey = AuthConfig.GetJwtKey(builder.Configuration, builder.Environment.IsProduction());
var jwtIssuer = builder.Configuration["HZone:Auth:Issuer"] ?? "HZone";
var jwtAudience = builder.Configuration["HZone:Auth:Audience"] ?? "HZone";

builder.Services.AddDbContext<HZoneDbContext>(options =>
{
    options.UseSqlite(connectionString);
    options.AddInterceptors(new HZone.Api.Data.SqlitePragmaInterceptor());
});
// 时间戳统一 UTC 序列化（带 Z）：EF SQLite 读回 Kind=Unspecified 默认丢 Z，
// 前端按本地时间解析导致显示偏移时区差（详见 .scratch/reviews/frontend-medium-batch.md 新发现节）
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
// 签发与校验复用同一来源的 JWT 键（AuthConfig 单点读取，生产未配置已 fail-fast）。
builder.Services.AddSingleton(jwtKey);
builder.Services.AddScoped<IContentQuery, ContentQuery>();
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<IEmailSender, FileEmailSender>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IFFmpegCompressor, FFmpegCompressor>();
// 自动备份：BackupRunner 纯逻辑（可单测）+ HostedService 调度（每 24h，保留 7 份）
builder.Services.AddSingleton<BackupRunner>();
builder.Services.AddHostedService<AutoBackupService>();
// 每周邮件周报（默认停用，HZone:Digest:Enabled=true 开启；周一北京 08:00）
builder.Services.AddSingleton<WeeklyDigestRunner>();
builder.Services.AddHostedService<WeeklyDigestService>();

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

// 认证端点限流：/auth/login 防暴力试密码、/auth/forgot-password 防刷盘（fixed window，分区键 = 远程 IP）。
// 已知简化：未启用 ForwardedHeaders 中间件，反代场景下所有请求共享反代 IP 的配额，先用 RemoteIpAddress 简单实现。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 项目 API 口径是 JSON：限流触发也回 JSON 错误体（默认是纯文本）。
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "too many requests" }, ct);
    };

    var loginPerMinute = builder.Configuration.GetValue("HZone:RateLimit:LoginPerMinute", 10);
    var forgotPerMinute = builder.Configuration.GetValue("HZone:RateLimit:ForgotPasswordPerMinute", 5);

    options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));
    options.AddPolicy("auth-forgot-password", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = forgotPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));
});

var app = builder.Build();

DatabaseBootstrapper.EnsureDatabase(app, connectionString);

app.UseAuthentication();
app.UseAuthorization();
// 限流中间件必须在路由解析之后（UseRouting 隐式调用）挂载，否则策略元数据读不到。
app.UseRateLimiter();

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
app.MapPlaylistEndpoints();
app.MapLedgerEndpoints();
app.MapReportEndpoints();
app.MapSearchAllEndpoints();
app.MapShareLinkEndpoints();

app.Run();

public partial class Program { }
