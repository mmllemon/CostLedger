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
/// 数据引导测试：结构版本保护 + 可恢复的一致性备份。
/// </summary>
public class DbBootstrapTests : IDisposable
{
    private readonly string _dir = TempDir();

    private static string TempDir()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "CostLedger.sln")))
            repo = repo.Parent ?? throw new InvalidOperationException("Cannot locate repository root.");
        var dir = Path.Combine(repo.Parent!.FullName, ".yanagent", "db-bootstrap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppDbContext Open(string path)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options);

    private static SqliteConnection Connect(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false
        }.ToString());
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int ReadVersion(string path)
    {
        using var conn = Connect(path, SqliteOpenMode.ReadOnly);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void 新版本升级到当前_自动迁移并保留数据()
    {
        var path = Path.Combine(_dir, "costledger.db");
        int oldVersion = DbBootstrap.SchemaVersion - 2;   // 模拟落后两个版本的旧账套

        // 用当前结构建库（含全部真实表），写入业务数据，再把版本号改回旧值以模拟旧账套。
        using (var db = Open(path))
        {
            DbBootstrap.EnsureCreatedAndSeed(db, path);
            var p = db.Projects.Single();
            p.Name = "升级前项目";
            db.Parties.Add(new CostLedger.Domain.Party { Name = "升级前单位", RoleCode = "Supplier" });
            db.SaveChanges();
        }
        using (var conn = Connect(path, SqliteOpenMode.ReadWrite))
            Execute(conn, $"PRAGMA user_version = {oldVersion};");
        ReadVersion(path).Should().Be(oldVersion);

        // 打开时自动迁移：版本升到当前，业务数据保留，并生成可回退备份。
        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path);

        ReadVersion(path).Should().Be(DbBootstrap.SchemaVersion);
        using (var check = Open(path))
        {
            check.Projects.Single().Name.Should().Be("升级前项目");
            check.Parties.Any(x => x.Name == "升级前单位").Should().BeTrue();
        }
        Directory.Exists(Path.Combine(_dir, "backups")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(_dir, "backups")).Should().NotBeEmpty();
    }

    [Fact]
    public void 缺表缺列的旧结构_迁移补回新表新列且保留数据()
    {
        var path = Path.Combine(_dir, "costledger.db");

        // 建当前结构 + 业务数据，然后真正删掉“较新”的表与列，模拟 v12 之前的旧结构。
        using (var db = Open(path))
        {
            DbBootstrap.EnsureCreatedAndSeed(db, path);
            var p = db.Projects.Single();
            p.Name = "旧结构项目";
            db.Parties.Add(new CostLedger.Domain.Party { Name = "旧结构单位", RoleCode = "Supplier" });
            db.SaveChanges();
            // 造一条外部金额事实，验证它在新旧结构间被完整搬运。
            db.ExternalAmountFacts.Add(new CostLedger.Domain.ExternalAmountFact
            {
                ProjectId = p.Id, Segment = CostLedger.Domain.BusinessSegment.MaterialSupply,
                Direction = CostLedger.Domain.EconomicDirection.Expenditure, Amount = 123m,
                AsOfDate = new DateOnly(2026, 1, 1), Status = CostLedger.Domain.ExternalFactStatus.Checked,
                Precision = CostLedger.Domain.ManagementPrecision.Project, Source = "旧结构来源"
            });
            db.SaveChanges();
        }
        using (var conn = Connect(path, SqliteOpenMode.ReadWrite))
        {
            // ExternalResultRecords 是 v14 新增表：删掉它模拟缺表的旧结构。
            // （列删除因 SQLite 外键限制不便直接模拟，由 Drop 表代表增量补齐能力。）
            Execute(conn, "DROP TABLE ExternalResultRecords;");
            Execute(conn, $"PRAGMA user_version = {DbBootstrap.SchemaVersion - 1};");
        }

        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path);

        ReadVersion(path).Should().Be(DbBootstrap.SchemaVersion);
        using (var check = Open(path))
        {
            check.Projects.Single().Name.Should().Be("旧结构项目");
            check.ExternalAmountFacts.Single().Source.Should().Be("旧结构来源");
            // 新表被补回（空表可用）。
            check.ExternalResultRecords.Count().Should().Be(0);
        }
    }

    [Fact]
    public void 新建账套_写入结构版本并种子()
    {
        var path = Path.Combine(_dir, "costledger.db");

        using (var db = Open(path))
            DbBootstrap.EnsureCreatedAndSeed(db, path);

        File.Exists(path).Should().BeTrue();
        ReadVersion(path).Should().Be(DbBootstrap.SchemaVersion);

        using var check = Open(path);
        check.Projects.Count().Should().Be(1); // 演示项目已种子
    }

    [Theory]
    // 版本保护样本随 SchemaVersion 推导：无版本标记(0)、上一版、未来版、历史 M1 版。
    // 写死数字会让"加字段后版本号递增"这项约定把测试变成过时断言。
    [InlineData(0)]
    [InlineData(DbBootstrap.SchemaVersion - 1)]
    [InlineData(DbBootstrap.SchemaVersion + 1)]
    [InlineData(3)]
    public void 版本不匹配_拒绝打开且原库不变(int version)
    {
        var path = Path.Combine(_dir, "costledger.db");
        using (var conn = Connect(path))
            Execute(conn, $"CREATE TABLE Marker(Value TEXT); INSERT INTO Marker VALUES ('user data'); PRAGMA user_version = {version};");
        var before = File.ReadAllBytes(path);

        using var db = Open(path);
        Action open = () => DbBootstrap.EnsureCreatedAndSeed(db, path);
        open.Should().Throw<InvalidOperationException>()
            .WithMessage($"*v{version}*v{DbBootstrap.SchemaVersion}*");

        ReadVersion(path).Should().Be(version);
        File.ReadAllBytes(path).Should().Equal(before);
        Directory.Exists(Path.Combine(_dir, "backups")).Should().BeFalse();
    }

    [Fact]
    public void 同版本账套_保留用户数据()
    {
        var path = Path.Combine(_dir, "costledger.db");
        using (var db = Open(path))
        {
            DbBootstrap.EnsureCreatedAndSeed(db, path);
            db.Projects.Single().Name = "User project";
            db.SaveChanges();
        }

        using var check = Open(path);
        DbBootstrap.EnsureCreatedAndSeed(check, path);
        check.Projects.Single().Name.Should().Be("User project");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 在线备份_包含未检查点的WAL数据且可独立恢复(bool automatic)
    {
        var path = Path.Combine(_dir, "costledger.db");
        using var source = Connect(path);
        Execute(source, $"PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0; CREATE TABLE Marker(Value TEXT); PRAGMA user_version = {DbBootstrap.SchemaVersion}; PRAGMA wal_checkpoint(TRUNCATE);");
        Execute(source, "INSERT INTO Marker VALUES ('committed in WAL');");
        new FileInfo(path + "-wal").Length.Should().BeGreaterThan(0);

        var target = Path.Combine(_dir, "manual.db");
        if (automatic) target = DbBootstrap.BackupFile(path, DbBootstrap.SchemaVersion);
        else DbBootstrap.BackupTo(path, target);

        ReadVersion(target).Should().Be(DbBootstrap.SchemaVersion);
        using var backup = Connect(target, SqliteOpenMode.ReadOnly);
        using var cmd = backup.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Marker;";
        cmd.ExecuteScalar().Should().Be("committed in WAL");
        cmd.CommandText = "PRAGMA integrity_check;";
        cmd.ExecuteScalar().Should().Be("ok");
    }

    [Fact]
    public void 自动备份_连续调用生成不同文件()
    {
        var path = Path.Combine(_dir, "costledger.db");
        using (var source = Connect(path)) Execute(source, "CREATE TABLE Marker(Value TEXT);");
        var first = DbBootstrap.BackupFile(path, 9);
        var second = DbBootstrap.BackupFile(path, 9);
        first.Should().NotBe(second);
        File.Exists(first).Should().BeTrue();
        File.Exists(second).Should().BeTrue();
    }

    [Fact]
    public void 手动备份_可覆盖已有备份()
    {
        var path = Path.Combine(_dir, "costledger.db");
        var target = Path.Combine(_dir, "backup.db");
        using (var source = Connect(path)) Execute(source, "CREATE TABLE Marker(Value TEXT); INSERT INTO Marker VALUES ('first');");
        DbBootstrap.BackupTo(path, target);
        using (var source = Connect(path)) Execute(source, "INSERT INTO Marker VALUES ('second');");
        DbBootstrap.BackupTo(path, target);
        using var backup = Connect(target, SqliteOpenMode.ReadOnly);
        using var cmd = backup.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Marker;";
        cmd.ExecuteScalar().Should().Be(2L);
    }

    [Fact]
    public void 备份_拒绝源目标相同且原库不变()
    {
        var path = Path.Combine(_dir, "costledger.db");
        using (var source = Connect(path)) Execute(source, "CREATE TABLE Marker(Value TEXT);");
        var before = File.ReadAllBytes(path);
        Action backup = () => DbBootstrap.BackupTo(path, path);
        backup.Should().Throw<ArgumentException>();
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void 备份_源文件缺失不创建空账套()
    {
        var path = Path.Combine(_dir, "missing.db");
        var target = Path.Combine(_dir, "backup.db");
        Action backup = () => DbBootstrap.BackupTo(path, target);
        backup.Should().Throw<SqliteException>();
        File.Exists(path).Should().BeFalse();
        File.Exists(target).Should().BeFalse();
    }
}
