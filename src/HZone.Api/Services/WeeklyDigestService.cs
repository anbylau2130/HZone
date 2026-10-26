using HZone.Api.Endpoints;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Services;

/// <summary>
/// 每周邮件周报调度：每周一北京 08:00 后的首次巡检发送（默认停用，
/// HZone:Digest:Enabled=true 开启）。幂等标记落 {DataDirectory}/digest-state.json
///（周起始键），重复巡检/服务重启不重发。
/// </summary>
public sealed class WeeklyDigestService : BackgroundService
{
    private readonly WeeklyDigestRunner _runner;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<WeeklyDigestService> _logger;

    public WeeklyDigestService(
        WeeklyDigestRunner runner, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<WeeklyDigestService> logger)
    {
        _runner = runner;
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bool.TryParse(_config["HZone:Digest:Enabled"] ?? "false", out var enabled) || !enabled)
        {
            return; // 默认停用：显式开启才发（避免开发/未预期打扰）
        }

        var dataDir = _config["HZone:DataDirectory"] ?? "data";
        var statePath = Path.Combine(dataDir, "digest-state.json");
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                if (DueNow(statePath, DateTime.UtcNow))
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
                    var sent = await _runner.SendAllAsync(db, DateTime.UtcNow, stoppingToken);
                    await File.WriteAllTextAsync(statePath, WeekKey(DateTime.UtcNow), stoppingToken);
                    _logger.LogInformation("周报已发 {Count} 人", sent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "周报巡检异常，下一小时重试");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)
                 && !stoppingToken.IsCancellationRequested);
    }

    /// <summary>到期判定：北京已过周一 08:00 且本周期未发（状态文件存最近已发周期键）。</summary>
    public static bool DueNow(string statePath, DateTime utcNow)
    {
        var zone = MeStatsEndpoints.ChinaZone();
        var nowBeijing = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone);
        var daysSinceMonday = ((int)nowBeijing.DayOfWeek + 6) % 7;
        var monday = nowBeijing.Date.AddDays(-daysSinceMonday);
        if (nowBeijing < monday.AddHours(8))
        {
            return false; // 周一 08:00 前不发
        }
        var weekKey = monday.ToString("yyyyMMdd");
        var last = File.Exists(statePath) ? File.ReadAllText(statePath).Trim() : string.Empty;
        return last != weekKey;
    }

    private static string WeekKey(DateTime utcNow)
    {
        var zone = MeStatsEndpoints.ChinaZone();
        var nowBeijing = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow, TimeSpan.Zero), zone);
        var daysSinceMonday = ((int)nowBeijing.DayOfWeek + 6) % 7;
        return nowBeijing.Date.AddDays(-daysSinceMonday).ToString("yyyyMMdd");
    }
}
