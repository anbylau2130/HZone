using System.IO.Compression;
using HZone.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace HZone.Tests;

/// <summary>
/// 整库备份执行器：VACUUM INTO 快照 + media 打包 + 保留清理 + 距上次备份时长。
/// 与 WebApplicationFactory 解耦（临时 SQLite 文件自建源库），纯文件系统断言。
/// </summary>
public class BackupRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDb;
    private readonly string _mediaDir;
    private readonly string _backupDir;
    private readonly BackupRunner _runner = new(NullLogger<BackupRunner>.Instance);

    public BackupRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hzone-backup-{Guid.NewGuid():N}");
        _sourceDb = Path.Combine(_root, "src", "hzone.db");
        _mediaDir = Path.Combine(_root, "src", "media");
        _backupDir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourceDb)!);
        Directory.CreateDirectory(_mediaDir);

        using var conn = new SqliteConnection($"Data Source={_sourceDb}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE Users (Id TEXT PRIMARY KEY, Nickname TEXT); INSERT INTO Users VALUES ('u1','Alice');";
        cmd.ExecuteNonQuery();
        File.WriteAllText(Path.Combine(_mediaDir, "aa.jpg"), "fake-image");
        File.WriteAllText(Path.Combine(_mediaDir, "bb.mp3"), "fake-audio");
    }

    [Fact]
    public void RunOnce_produces_zip_with_snapshot_and_media()
    {
        _runner.RunOnce($"Data Source={_sourceDb}", _mediaDir, _backupDir);

        var zipPath = Directory.EnumerateFiles(_backupDir, "hzone-backup-*.zip").Single();
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        Assert.Contains("hzone.db", names);
        Assert.Contains("media/aa.jpg", names);
        Assert.Contains("media/bb.mp3", names);

        // 快照可打开且数据完整（VACUUM INTO 产物是合法 SQLite 库）
        var entry = zip.GetEntry("hzone.db")!;
        var extracted = Path.Combine(_root, "extracted.db");
        entry.ExtractToFile(extracted);
        using var check = new SqliteConnection($"Data Source={extracted}");
        check.Open();
        using var q = check.CreateCommand();
        q.CommandText = "SELECT Nickname FROM Users WHERE Id='u1'";
        Assert.Equal("Alice", q.ExecuteScalar() as string);
    }

    [Fact]
    public void RunOnce_failure_leaves_no_partial_zip()
    {
        // 源库不存在 → VACUUM 抛错 → 备份目录不应留半成品 zip / 快照临时文件
        Assert.Throws<SqliteException>(() =>
            _runner.RunOnce("Data Source=" + Path.Combine(_root, "nope.db"), _mediaDir, _backupDir));
        Assert.Empty(Directory.EnumerateFiles(_backupDir, "*.zip"));
        Assert.Empty(Directory.EnumerateFiles(_backupDir, ".snapshot-*"));
    }

    [Fact]
    public void PruneOldBackups_keeps_latest_retain_count()
    {
        Directory.CreateDirectory(_backupDir);
        // 造 9 个时间戳递增的旧 zip
        for (var i = 1; i <= 9; i++)
        {
            File.WriteAllText(Path.Combine(_backupDir, $"hzone-backup-2026010{i}-0300.zip"), "x");
        }
        File.WriteAllText(Path.Combine(_backupDir, "unrelated.zip"), "x");

        var removed = _runner.PruneOldBackups(_backupDir, 7);

        Assert.Equal(2, removed);
        var remaining = Directory.EnumerateFiles(_backupDir, "hzone-backup-*.zip").OrderBy(p => p).ToList();
        Assert.Equal(7, remaining.Count);
        // 留最新 7 个（4~9 日），最早两个被删；无关文件不动
        Assert.DoesNotContain(Path.Combine(_backupDir, "hzone-backup-20260101-0300.zip"), remaining);
        Assert.Contains(Path.Combine(_backupDir, "hzone-backup-20260109-0300.zip"), remaining);
        Assert.True(File.Exists(Path.Combine(_backupDir, "unrelated.zip")));
    }

    [Fact]
    public void SinceLastBackup_empty_dir_returns_max_and_recent_returns_small()
    {
        Assert.Equal(TimeSpan.MaxValue, BackupRunner.SinceLastBackup(_backupDir));
        Directory.CreateDirectory(_backupDir);
        Assert.Equal(TimeSpan.MaxValue, BackupRunner.SinceLastBackup(_backupDir));

        var fresh = Path.Combine(_backupDir, "hzone-backup-20260101-0300.zip");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddMinutes(-30));
        var since = BackupRunner.SinceLastBackup(_backupDir);
        Assert.True(since >= TimeSpan.FromMinutes(29) && since < TimeSpan.FromMinutes(31));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
