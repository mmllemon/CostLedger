using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 数据引导测试：结构版本记录 + 升级前自动备份（不静默丢数据）。
/// </summary>
public class DbBootstrapTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "costledger_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AppDbContext Open(string path)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    private static int ReadVersion(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void 新建账套_写入结构版本并种子()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "costledger.db");

        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path);

        File.Exists(path).Should().BeTrue();
        ReadVersion(path).Should().Be(DbBootstrap.SchemaVersion);

        using var check = Open(path);
        check.Projects.Count().Should().Be(1); // 演示项目已种子
    }

    [Fact]
    public void 结构升级_先备份旧库再重建()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "costledger.db");

        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path);

        // 模拟"旧版本账套"：把结构版本改成 0
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 0;";
            cmd.ExecuteNonQuery();
        }

        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path); // 触发升级

        var backups = Directory.GetFiles(Path.Combine(dir, "backups"), "*.db");
        backups.Should().HaveCount(1);                 // 旧库已备份
        ReadVersion(path).Should().Be(DbBootstrap.SchemaVersion);
    }
}
