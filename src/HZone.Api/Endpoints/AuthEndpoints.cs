using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HZone.Api.Endpoints;

public static class AuthEndpoints
{
    private const string FrontendBaseUrl = "http://localhost:5173";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            HZoneDbContext db,
            IPasswordHasher hasher,
            IEmailSender emailSender,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

            var errors = new Dictionary<string, string[]>();
            var invite = await db.InviteCodes.FirstOrDefaultAsync(i => i.Code == request.InviteCode, ct);
            if (invite is null)
            {
                errors["inviteCode"] = new[] { "invalid invite code" };
            }
            else if (invite.UsedAt is not null)
            {
                errors["inviteCode"] = new[] { "invite code already used" };
            }
            else if (invite.ExpiresAt <= now)
            {
                errors["inviteCode"] = new[] { "invite code expired" };
            }

            if (!new EmailAddressAttribute().IsValid(email))
            {
                errors["email"] = new[] { "invalid email" };
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                errors["password"] = new[] { "password must be at least 8 characters" };
            }

            if (string.IsNullOrWhiteSpace(request.Nickname))
            {
                errors["nickname"] = new[] { "nickname is required" };
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "email already registered");
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = hasher.Hash(request.Password),
                Nickname = request.Nickname.Trim(),
                EmailVerified = false,
                CreatedAt = now,
            };

            // errors 为空意味着 invite 一定非空（invite 为空必产生 inviteCode 字段错误）
            invite!.UsedById = user.Id;
            invite.UsedAt = now;
            db.Users.Add(user);
            db.EmailTokens.Add(new EmailToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = EmailTokenPurpose.VerifyEmail,
                Token = token,
                ExpiresAt = now.AddHours(24),
            });
            await db.SaveChangesAsync(ct);

            await emailSender.SendAsync(user.Email, "Verify your email",
                $"Welcome to HZone! Verify your email: {FrontendBaseUrl}/#/verify-email?token={token}", ct);

            return Results.Created("/auth/me", new
            {
                userId = user.Id,
                email = user.Email,
                nickname = user.Nickname,
                emailVerified = false,
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/auth/verify-email", async (
            VerifyEmailRequest request,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var emailToken = await db.EmailTokens.FirstOrDefaultAsync(
                t => t.Token == request.Token && t.Purpose == EmailTokenPurpose.VerifyEmail, ct);
            if (emailToken is null || emailToken.UsedAt is not null || emailToken.ExpiresAt <= now)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "invalid or expired token");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == emailToken.UserId, ct);
            if (user is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "invalid or expired token");
            }

            user.EmailVerified = true;
            emailToken.UsedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/auth/login", async (
            LoginRequest request,
            HZoneDbContext db,
            IPasswordHasher hasher,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null || user.IsDisabled || !user.EmailVerified ||
                !hasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "invalid credentials");
            }

            var (token, expiresAt) = MintJwt(user, config);
            return Results.Ok(new
            {
                token,
                expiresAt,
                user = new { id = user.Id, email = user.Email, nickname = user.Nickname, isAdmin = user.IsAdmin },
            });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapGet("/auth/me", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(principal);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                return Results.NotFound(new { error = "user not found" });
            }

            return Results.Ok(new { id = user.Id, email = user.Email, nickname = user.Nickname, isAdmin = user.IsAdmin });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/auth/forgot-password", async (
            ForgotPasswordRequest request,
            HZoneDbContext db,
            IEmailSender emailSender,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == email && u.EmailVerified && !u.IsDisabled, ct);
            if (user is not null)
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                db.EmailTokens.Add(new EmailToken
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Purpose = EmailTokenPurpose.ResetPassword,
                    Token = token,
                    ExpiresAt = now.AddHours(1),
                });
                await db.SaveChangesAsync(ct);
                await emailSender.SendAsync(user.Email, "Reset your password",
                    $"Reset your password: {FrontendBaseUrl}/#/reset-password?token={token}", ct);
            }

            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/auth/reset-password", async (
            ResetPasswordRequest request,
            HZoneDbContext db,
            IPasswordHasher hasher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = new[] { "password must be at least 8 characters" },
                });
            }

            var now = DateTime.UtcNow;
            var emailToken = await db.EmailTokens.FirstOrDefaultAsync(
                t => t.Token == request.Token && t.Purpose == EmailTokenPurpose.ResetPassword, ct);
            if (emailToken is null || emailToken.UsedAt is not null || emailToken.ExpiresAt <= now)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "invalid or expired token");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == emailToken.UserId, ct);
            if (user is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "invalid or expired token");
            }

            user.PasswordHash = hasher.Hash(request.NewPassword);
            emailToken.UsedAt = now;
            var staleTokens = await db.EmailTokens
                .Where(t => t.UserId == user.Id &&
                    t.Purpose == EmailTokenPurpose.ResetPassword &&
                    t.UsedAt == null &&
                    t.Id != emailToken.Id)
                .ToListAsync(ct);
            foreach (var stale in staleTokens)
            {
                stale.UsedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static (string Token, DateTime ExpiresAt) MintJwt(User user, IConfiguration config)
    {
        var key = config["HZone:Auth:JwtKey"] ?? "dev-only-insecure-jwt-key-0246802468";
        var issuer = config["HZone:Auth:Issuer"] ?? "HZone";
        var audience = config["HZone:Auth:Audience"] ?? "HZone";
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var claims = new List<Claim> { new("sub", user.Id.ToString()), new("nickname", user.Nickname) };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expiresAt, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public sealed record RegisterRequest(string InviteCode, string Email, string Password, string Nickname);
public sealed record VerifyEmailRequest(string Token);
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
