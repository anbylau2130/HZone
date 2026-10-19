using System.ComponentModel.DataAnnotations;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/setup/status", async (HZoneDbContext db, CancellationToken ct) =>
            Results.Ok(new { initialized = await SetupState.IsInitializedAsync(db, ct) }));

        app.MapPost("/setup/admin", async (
            CreateAdminRequest request,
            HZoneDbContext db,
            IPasswordHasher hasher,
            CancellationToken ct) =>
        {
            if (await SetupState.IsInitializedAsync(db, ct))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "instance already initialized");
            }

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Email) ||
                !new EmailAddressAttribute().IsValid(request.Email))
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

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = hasher.Hash(request.Password),
                Nickname = request.Nickname.Trim(),
                EmailVerified = true,
                IsAdmin = true,
                CreatedAt = now,
            };

            db.Users.Add(user);
            db.SystemConfigs.Add(new SystemConfig { Key = SetupState.SetupCompletedKey, Value = now.ToString("O") });
            await db.SaveChangesAsync(ct);

            return Results.Created("/setup/status", new AdminResponse(user.Id, user.Email, user.Nickname));
        });
    }
}

public sealed record CreateAdminRequest(string Email, string Password, string Nickname);

public sealed record AdminResponse(Guid Id, string Email, string Nickname);
