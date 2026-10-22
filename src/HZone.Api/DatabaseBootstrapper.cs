using HZone.Infrastructure;
using Microsoft.Data.Sqlite;
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
        db.Database.OpenConnection();
        EnableSqliteConcurrencyPragmas(app.Logger, db);
        BaselineLegacyDatabaseIfNeeded(app.Logger, db);
        db.Database.Migrate();
    }

    /// <summary>
    /// SQLite 并发三件套：WAL（读写不互斥）+ busy_timeout（写锁等待而非立刻报 locked）
    /// + foreign_keys（EF 关系依赖级联）。连接串 cache=shared 保证同进程多连接看到同一 WAL。
    /// </summary>
    private static void EnableSqliteConcurrencyPragmas(ILogger logger, HZoneDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var pragmas = new[]
        {
            "PRAGMA journal_mode = WAL",
            "PRAGMA busy_timeout = 5000",
            "PRAGMA foreign_keys = ON"
        };
        foreach (var pragma in pragmas)
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            var result = command.ExecuteScalar()?.ToString();
            logger.LogInformation("SQLite PRAGMA {Pragma} → {Result}", pragma, result);
        }
    }

    /// <summary>
    /// 存量 dev 库经 EnsureCreated+手工 ALTER 建成，无 __EFMigrationsHistory。
    /// 检测到「有业务表但无迁移历史」时自动 baseline：标记 InitialCreate 已应用，
    /// 让后续 Migrate() 只应用增量迁移（存量 dev 数据可弃，结构以当前模型为准）。
    /// </summary>
    private static void BaselineLegacyDatabaseIfNeeded(ILogger logger, HZoneDbContext db)
    {
        db.Database.OpenConnection();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Users', '__EFMigrationsHistory')";
            using var reader = command.ExecuteReader();
            var tables = new List<string>();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            if (!tables.Contains("Users") || tables.Contains("__EFMigrationsHistory"))
            {
                return; // 全新库或已迁移库，直接走 Migrate()
            }
        }

        // legacy baseline 语义：存量库的结构对应首个建表迁移（InitialCreate），标记其后让
        // Migrate() 只应用增量。迁移已有多个（InitialCreate + 后续增量），不能 Single()。
        var initialMigrationId = db.Database.GetMigrations()
            .FirstOrDefault(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal))
            ?? db.Database.GetMigrations().First();
        logger.LogWarning(
            "检测到 legacy 数据库（无 __EFMigrationsHistory 但已有业务表），自动 baseline 迁移 {MigrationId} 后继续增量迁移",
            initialMigrationId);

        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw(
            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({Quote(initialMigrationId)}, '8.0.11')");
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
