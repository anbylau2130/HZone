using HZone.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HZone.Tests.Helpers;

/// <summary>
/// 测试服务器工厂。ffmpegAvailable 强制指定 ffmpeg 可用性（跳过探测，e2e 确定性）。
/// </summary>
public sealed class HZoneApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-only-jwt-key-0123456789abcdef";
    public const string JwtIssuer = "HZone";
    public const string JwtAudience = "HZone";

    private readonly string _dataDirectory;
    private readonly bool? _ffmpegAvailable;

    public HZoneApiFactory() : this(null)
    {
    }

    internal HZoneApiFactory(bool? ffmpegAvailable)
    {
        _ffmpegAvailable = ffmpegAvailable;
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
        if (_ffmpegAvailable is { } ffmpeg)
        {
            builder.UseSetting(FFmpegCompressor.AvailableSettingKey, ffmpeg.ToString());
        }
    }
}

/// <summary>
/// IClassFixture 需要无参构造，包一层固定"无 ffmpeg"环境。
/// </summary>
public sealed class NoFFmpegApiFactory : IDisposable
{
    public HZoneApiFactory Factory { get; } = new(false);

    public void Dispose() => Factory.Dispose();
}
