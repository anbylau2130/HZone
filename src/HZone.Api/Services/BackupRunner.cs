using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HZone.Api.Services;

/// <summary>
/// 整库备份执行器（与调度解耦，可单测）：
/// SQLite VACUUM INTO 生成一致性快照（WAL 下直接拷 db 文件会撕裂）+
/// media 目录整体打包为 zip。zip = {db 快照, media/**}，恢复即整站恢复
///（内容导出 zip 只含正文，丢账号/好友/PAT——不用于备份）。
/// </summary>
public sealed class BackupRunner
{
    private readonly ILogger<BackupRunner> _logger;

    public BackupRunner(ILogger<BackupRunner> logger) => _logger = logger;

    private static readonly Regex BackupFileName =
        new(@"^hzone-backup-\d{8}-\d{4}\.zip$", RegexOptions.Compiled);

    /// <summary>执行一次备份：快照源库 + 打包 media → backupDir/hzone-backup-{yyyyMMdd-HHmm}.zip。</summary>
    public void RunOnce(string sourceConnectionString, string mediaDirectory, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var zipPath = Path.Combine(backupDirectory, $"hzone-backup-{stamp}.zip");
        var tempDb = Path.Combine(backupDirectory, $".snapshot-{Guid.NewGuid():N}.db");
        try
        {
            // VACUUM INTO：在线一致性快照（备份目标不能已存在）。
            // 源连接强制 ReadOnly：Sqlite 默认 ReadWriteCreate，连接串配错指向
            // 不存在的库时会静默建空库——备份出空 zip 还报成功（测试实测踩中）
            var cs = sourceConnectionString.Contains("Mode=", StringComparison.OrdinalIgnoreCase)
                ? sourceConnectionString
                : sourceConnectionString.TrimEnd(';') + ";Mode=ReadOnly";
            using (var source = new SqliteConnection(cs))
            {
                source.Open();
                using var cmd = source.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{tempDb.Replace("'", "''")}'";
                cmd.ExecuteNonQuery();
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempDb, "hzone.db");
                if (Directory.Exists(mediaDirectory))
                {
                    // storedName 已过 LocalFileStorage 的字符白名单，媒体文件平铺进 media/
                    foreach (var file in Directory.EnumerateFiles(mediaDirectory))
                    {
                        zip.CreateEntryFromFile(file, $"media/{Path.GetFileName(file)}");
                    }
                }
            }
            _logger.LogInformation("自动备份完成：{Path}（{Size} KB）", zipPath, SizeKb(zipPath));
        }
        catch (Exception ex)
        {
            // 半成品清理：不留损坏 zip（会被「最新备份时间」误判为成功）
            TryDelete(zipPath);
            _logger.LogError(ex, "自动备份失败：{Message}", ex.Message);
            throw;
        }
        finally
        {
            TryDelete(tempDb);
        }
    }

    /// <summary>保留策略：按文件名时间戳序保留最新 retainCount 个，删除更早的。</summary>
    public int PruneOldBackups(string backupDirectory, int retainCount)
    {
        if (!Directory.Exists(backupDirectory) || retainCount < 1) return 0;
        var olds = Directory.EnumerateFiles(backupDirectory, "hzone-backup-*.zip")
            .Where(p => BackupFileName.IsMatch(Path.GetFileName(p)))
            .OrderByDescending(p => p)
            .Skip(retainCount)
            .ToList();
        foreach (var path in olds)
        {
            TryDelete(path);
        }
        return olds.Count;
    }

    /// <summary>距上次成功备份的时长（无备份返回无穷大，触发首次备份）。</summary>
    public static TimeSpan SinceLastBackup(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory)) return TimeSpan.MaxValue;
        var latest = Directory.EnumerateFiles(backupDirectory, "hzone-backup-*.zip")
            .Where(p => BackupFileName.IsMatch(Path.GetFileName(p)))
            .Select(p => File.GetLastWriteTimeUtc(p))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return latest == DateTime.MinValue ? TimeSpan.MaxValue : DateTime.UtcNow - latest;
    }

    private static long SizeKb(string path) => new FileInfo(path).Length / 1024;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 清理失败不影响主流程（下轮覆盖）
        }
    }
}
