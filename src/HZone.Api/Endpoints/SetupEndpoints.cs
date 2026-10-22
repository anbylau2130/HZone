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

            // 防并发抢注：check-then-act 之间公网可能并发插第二个 IsAdmin 账号。
            // SystemConfig.Key 是主键（唯一），「创建管理员 + setup.completed 标记」在同一
            // 事务里提交，并发第二个提交会因主键冲突失败 → 复核后按已初始化拒绝。
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                db.Users.Add(user);
                db.SystemConfigs.Add(new SystemConfig { Key = SetupState.SetupCompletedKey, Value = now.ToString("O") });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // 主键/唯一索引冲突落到这里；复核确认已初始化 → 竞态败者，回滚拒绝。
                if (await SetupState.IsInitializedAsync(db, ct))
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "instance already initialized");
                }

                throw;
            }

            return Results.Created("/setup/status", new AdminResponse(user.Id, user.Email, user.Nickname));
        });
    }
}

public sealed record CreateAdminRequest(string Email, string Password, string Nickname);

public sealed record AdminResponse(Guid Id, string Email, string Nickname);
