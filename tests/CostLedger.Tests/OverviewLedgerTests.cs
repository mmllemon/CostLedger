using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 阶段 A 测试：项目总览（一屏核心数字）与台账引擎（明细+合计）。
/// </summary>
public class OverviewLedgerTests
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

    private static decimal Dec(string s) => decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);

    [Fact]
    public async Task 项目总览_核心数字()
    {
        using var db = CreateSeededContext();
        var o = await new ProjectOverviewService(db, new CostSpineService(db)).BuildAsync(1);

        o.UpstreamContract.Should().Be(12_480_000m);
        o.DownstreamContract.Should().Be(4_633_000m);
        o.ReceivedCumulative.Should().Be(0m);
        o.Receivable.Should().Be(12_480_000m);     // 对上合同额 − 累计收款
        o.Payable.Should().Be(4_633_000m);         // 对下合同额 − 累计付款
        o.Target.Should().Be(4_633_000m);
        o.EstimatedFinal.Should().Be(4_770_000m);  // 200w + 227w + 50w
        o.GrossProfit.Should().Be(7_710_000m);     // 1248w − 477w
    }

    [Fact]
    public async Task 合同台账_明细与合计()
    {
        using var db = CreateSeededContext();
        var r = await new LedgerService(db).BuildAsync(1, LedgerKind.Contract);

        r.Rows.Should().HaveCount(5);
        Dec(r.Footers[0][4]).Should().Be(17_113_000m); // 合计金额在第 5 列
    }

    [Fact]
    public async Task 变更签证台账_正负与净额()
    {
        using var db = CreateSeededContext();
        db.ChangeOrders.Add(new ChangeOrder { Code = "CO-A", ProjectId = 1, Title = "增加电缆敷设", Amount = 100_000m, Type = ChangeOrderType.SiteVariation, Status = DocStatus.Confirmed, OrderDate = new DateOnly(2026, 3, 1) });
        db.ChangeOrders.Add(new ChangeOrder { Code = "CO-B", ProjectId = 1, Title = "减少基础", Amount = -30_000m, Type = ChangeOrderType.DesignChange, Status = DocStatus.Draft, OrderDate = new DateOnly(2026, 3, 2) });
        await db.SaveChangesAsync();

        var r = await new LedgerService(db).BuildAsync(1, LedgerKind.ChangeOrder);

        r.Rows.Should().HaveCount(2);
        Dec(r.Footers[0][3]).Should().Be(100_000m);   // 正变更合计
        Dec(r.Footers[1][3]).Should().Be(-30_000m);   // 负变更合计
        Dec(r.Footers[2][3]).Should().Be(70_000m);    // 净额
    }
}
