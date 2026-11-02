using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HZone.Api;

/// <summary>
/// 每个连接打开时执行 SQLite 并发 PRAGMA：
/// WAL 由 Bootstrapper 设一次（文件级持久）；busy_timeout 让写锁等待而非立刻报 locked；
/// foreign_keys=ON 使数据库层强制引用完整性（EF 关系级联依赖它）。
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply((SqliteConnection)connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply((SqliteConnection)connection);
        return Task.CompletedTask;
    }

    private static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
    }
}
