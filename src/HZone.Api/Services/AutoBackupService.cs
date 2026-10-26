namespace HZone.Api.Services;

/// <summary>
/// 自动备份调度（IHostedService）：每小时醒一次，距上次备份超过间隔（默认 24h）
/// 则执行 BackupRunner + 保留清理。取消「记得手动导出」环节——私人日记库的
/// 数据安全不该依赖用户记性。配置（appsettings/环境变量）：
/// HZone:Backup:Enabled（默认 true）/ Directory（默认 {DataDirectory}/backups）/
/// IntervalHours（默认 24）/ RetainCount（默认 7）。
/// </summary>
public sealed class AutoBackupService : BackgroundService
{
    private readonly BackupRunner _runner;
    private readonly IConfiguration _config;
    private readonly ILogger<AutoBackupService> _logger;

    public AutoBackupService(BackupRunner runner, IConfiguration config, ILogger<AutoBackupService> logger)
    {
        _runner = runner;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bool.TryParse(_config["HZone:Backup:Enabled"] ?? "true", out var enabled) || !enabled)
        {
            _logger.LogInformation("自动备份已停用（HZone:Backup:Enabled=false）");
            return;
        }

        var interval = TimeSpan.FromHours(DoubleConfig("HZone:Backup:IntervalHours", 24));
        var retain = (int)DoubleConfig("HZone:Backup:RetainCount", 7);
        var dataDir = _config["HZone:DataDirectory"] ?? "data";
        var backupDir = _config["HZone:Backup:Directory"] ?? Path.Combine(dataDir, "backups");
        var mediaDir = Path.Combine(dataDir, "media");
        var connectionString = _config["HZone:Database:ConnectionString"] ?? $"Data Source={Path.Combine(dataDir, "hzone.db")}";

        // 启动即查（服务重启不该重置 24h 节奏：仍按「最新备份时间」判定），
        // 之后每小时巡检（间隔缩短到 1h 粒度，错过后下次巡检补上）
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                if (BackupRunner.SinceLastBackup(backupDir) >= interval)
                {
                    _runner.RunOnce(connectionString, mediaDir, backupDir);
                    _runner.PruneOldBackups(backupDir, retain);
                }
            }
            catch (Exception ex)
            {
                // RunOnce 已记错误日志；此处兜底防timer循环中断
                _logger.LogWarning(ex, "备份巡检异常，下一小时重试");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)
                 && !stoppingToken.IsCancellationRequested);
    }

    private double DoubleConfig(string key, double fallback) =>
        double.TryParse(_config[key], out var v) && v > 0 ? v : fallback;
}
