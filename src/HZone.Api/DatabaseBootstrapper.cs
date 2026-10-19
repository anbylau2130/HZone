using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api;

public static class DatabaseBootstrapper
{
    public static void EnsureDatabase(WebApplication app, string connectionString)
    {
        var dataSource = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 &&
                parts[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(dataSource))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dataSource))!);
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
        db.Database.EnsureCreated();
    }
}
