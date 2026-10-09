using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 报表引擎测试：用内存 SQLite + 种子，验证列投影、聚合与筛选（M2-C 报表中心核心）。
/// </summary>
public class ReportServiceTests
{
    private static AppDbContext CreateSeededContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        SeedData.SeedIfEmpty(db);
        return db;
    }

    [Fact]
    public async Task 运行合同台账_列与金额合计正确()
    {
        using var db = CreateSeededContext();
        var svc = new ReportService(db);
        var def = (await svc.ListReportsAsync()).Single(r => r.Code == "CONTRACT-LEDGER");

        var result = svc.Run(def);

        result.Headers.Should().Contain("合同编号").And.Contain("合同金额");
        result.Rows.Should().HaveCount(4); // 种子 4 份合同
        var total = decimal.Parse(result.Footers[3].Replace(",", ""), CultureInfo.InvariantCulture);
        total.Should().Be(4_633_000m); // 186.3w + 129w + 98w + 50w
    }

    [Fact]
    public void 筛选_金额大于一百万_仅两行()
    {
        using var db = CreateSeededContext();
        var svc = new ReportService(db);
        var def = new ReportDef { Code = "T", Name = "T", Title = "T", DataSourceCode = "Contract" };
        def.Columns.Add(new ReportColumn { FieldKey = "Code", Label = "编号", Visible = true, DisplayOrder = 1 });
        def.Columns.Add(new ReportColumn { FieldKey = "Amount", Label = "金额", Visible = true, DisplayOrder = 2 });
        def.Filters.Add(new ReportFilter { FieldKey = "Amount", Operator = FilterOperator.GreaterThan, Value = "1000000" });

        var result = svc.Run(def);

        result.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task 保存并重载_定义往返一致()
    {
        using var db = CreateSeededContext();
        var svc = new ReportService(db);
        var def = new ReportDef { Code = "RPT-X", Name = "自定义", Title = "自定义标题", DataSourceCode = "Project" };
        def.Columns.Add(new ReportColumn { FieldKey = "Code", Label = "项目编号", Visible = true, DisplayOrder = 1, Aggregate = AggregateType.Count });

        var id = await svc.SaveReportAsync(def);
        var reloaded = (await svc.ListReportsAsync()).Single(r => r.Id == id);

        reloaded.Name.Should().Be("自定义");
        reloaded.DataSourceCode.Should().Be("Project");
        reloaded.Columns.Should().ContainSingle(c => c.FieldKey == "Code" && c.Aggregate == AggregateType.Count);
    }
}
