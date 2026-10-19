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
using HZone.Api.Endpoints;
using HZone.Api.Services;

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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

DatabaseBootstrapper.EnsureDatabase(app, connectionString);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapContentEndpoints();
app.MapCommentEndpoints();
app.MapInboxEndpoints();
app.MapMediaEndpoints();
app.MapFriendEndpoints();
app.MapSetupEndpoints();
app.MapInviteEndpoints();
app.MapAuthEndpoints();

app.Run();

public partial class Program { }
