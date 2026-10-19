using HZone.Api;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests.Helpers;

public static class SeedExtensions
{
    /// <summary>
    /// 初始化实例（创建管理员 + 写入 setup.completed 标记）。幂等：已初始化则直接返回 null。
    /// </summary>
    public static async Task<User?> SeedSetupAsync(this HZoneApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        if (await SetupState.IsInitializedAsync(db))
        {
            return null;
        }

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@test.local",
            PasswordHash = "test-hash",
            Nickname = "Admin",
            EmailVerified = true,
            IsAdmin = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(admin);
        db.SystemConfigs.Add(new SystemConfig
        {
            Key = SetupState.SetupCompletedKey,
            Value = DateTime.UtcNow.ToString("O"),
        });
        await db.SaveChangesAsync();
        return admin;
    }

    public static async Task<User> SeedUserAsync(
        this HZoneApiFactory factory, string email, string nickname)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = "test-hash",
            Nickname = nickname,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task SeedFriendshipAsync(
        this HZoneApiFactory factory, User requester, User addressee)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = requester.Id,
            AddresseeId = addressee.Id,
            Status = FriendshipStatus.Accepted,
            CreatedAt = DateTime.UtcNow,
            AcceptedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
