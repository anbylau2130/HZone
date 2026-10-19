using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HZone.Tests.Helpers;

public sealed class HZoneApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-only-jwt-key-0123456789abcdef";
    public const string JwtIssuer = "HZone";
    public const string JwtAudience = "HZone";

    private readonly string _dataDirectory;

    public HZoneApiFactory()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "hzone-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
    }

    public string DataDirectory => _dataDirectory;

    public string DbPath => Path.Combine(_dataDirectory, "hzone.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("HZone:Database:ConnectionString", $"Data Source={DbPath}");
        builder.UseSetting("HZone:DataDirectory", _dataDirectory);
        builder.UseSetting("HZone:Auth:JwtKey", JwtKey);
    }
}
