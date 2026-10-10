using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CostLedger.Tests;

/// <summary>
/// 用用户填写的真实数据表跑一遍导入，把领域规则对真实数据的裁决打印出来。
/// 这不是把表格当测试通过门槛，而是让"模型装不装得下真实业务"变成可复核的证据。
/// </summary>
public class RealDataImportTests
{
    private readonly ITestOutputHelper _out;
    public RealDataImportTests(ITestOutputHelper output) => _out = output;

    private static AppDbContext CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task 导入用户填写的真实数据并输出逐条结果与项目摘要()
    {
        using var db = CreateContext();
        SeedData.SeedMetadataOnly(db);

        var dir = Path.Combine(AppContext.BaseDirectory, "Import");
        Directory.Exists(dir).Should().BeTrue($"导入目录应存在：{dir}");

        var report = await new EconomicDataImporter(db).ImportFromDirectoryAsync(dir);

        _out.WriteLine($"== 导入结果：接受 {report.Imported} 条，拒绝 {report.Rejected} 条 ==");
        foreach (var e in report.Entries)
            _out.WriteLine($"[{(e.Imported ? "OK " : "拒 ")}] {e.Table} | {e.Key} | {e.Message}");

        _out.WriteLine("");
        _out.WriteLine("== 项目经济摘要 ==");
        foreach (var s in report.ProjectSummaries)
            _out.WriteLine(s);

        _out.WriteLine("");
        _out.WriteLine("== 被拒绝的记录（领域规则对真实数据的裁决）==");
        foreach (var e in report.Rejections)
            _out.WriteLine($"[拒] {e.Table} | {e.Key} | {e.Message}");

        // 项目与单位应被接受
        (await db.Projects.CountAsync()).Should().BeGreaterThanOrEqualTo(2);
        (await db.CostControlItems.CountAsync()).Should().BeGreaterThanOrEqualTo(4);
        // 至少能给出两个项目的摘要
        report.ProjectSummaries.Should().HaveCountGreaterThanOrEqualTo(2);

        // 修正后的数据应全部通过领域规则校验；任何拒绝都说明表内交叉引用又不一致了。
        report.Rejected.Should().Be(0,
            "数据表应自洽；被拒记录：" + string.Join(" | ", report.Rejections.Select(r => $"{r.Table}/{r.Key}: {r.Message}")));
    }

    [Fact]
    public async Task 生成的真实数据账套可被桌面引导路径打开()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Import");
        var dbPath = Path.Combine(Path.GetTempPath(), $"costledger_realdata_{Guid.NewGuid():N}.db");
        try
        {
            var (path, report) = await EconomicDataImporter.ImportToFileDatabaseAsync(dbPath, dir);
            report.Rejected.Should().Be(0);
            File.Exists(path).Should().BeTrue();

            // 桌面程序打开账套走的就是 DbBootstrap.EnsureCreatedAndSeed：版本匹配则不抛异常。
            var options = DbBootstrap.Configure(path);
            using (var db = new AppDbContext(options))
            {
                DbBootstrap.EnsureCreatedAndSeed(db, path);   // 不兼容会抛 InvalidOperationException
                (await db.Projects.CountAsync()).Should().Be(2);
                (await db.ExternalAmountFacts.CountAsync()).Should().Be(2);
                (await db.CostForecastVersions.CountAsync()).Should().Be(2);
            }
        }
        finally
        {
            // SQLite 连接池会持有文件句柄，先清池再删。
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
