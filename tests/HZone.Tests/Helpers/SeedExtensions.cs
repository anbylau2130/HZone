using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests.Helpers;

public static class SeedExtensions
{
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
