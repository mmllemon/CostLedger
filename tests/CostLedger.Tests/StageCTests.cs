using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>阶段 C 测试：台账派生指标（未收/收款比例/未付/变更占比）并入 KPI，驱动预警。</summary>
public class StageCTests
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

    private static DashboardService Dash(AppDbContext db) => new(db, new CostSpineService(db));

    [Fact]
    public async Task 台账派生指标_未收与收款比例与未付()
    {
        using var db = CreateSeededContext();
        var d = Dash(db);

        (await d.GetMetricAsync(1, "Receivable")).Should().Be(12_480_000m); // 对上合同额 − 0
        (await d.GetMetricAsync(1, "ReceivedRatio")).Should().Be(0m);
        (await d.GetMetricAsync(1, "Payable")).Should().Be(4_633_000m);     // 对下合同额 − 0
    }

    [Fact]
    public async Task 变更净额占比_达阈值则触发预警()
    {
        using var db = CreateSeededContext();
        // 变更净额 150 万 / 对上合同额 1248 万 ≈ 12.0% ≥ 10%
        db.ChangeOrders.Add(new ChangeOrder { Code = "CO-C1", ProjectId = 1, Title = "重大变更", Amount = 1_500_000m, Type = ChangeOrderType.DesignChange, Status = DocStatus.Confirmed, OrderDate = new DateOnly(2026, 4, 1) });
        await db.SaveChangesAsync();

        (await Dash(db).GetMetricAsync(1, "ChangeRatio")).Should().BeGreaterThan(0.10m);

        var alerts = new AlertService(db, Dash(db));
        var created = await alerts.EvaluateAsync();
        created.Should().Be(4); // 超支科目/总偏差/未收/变更占比 均触发
    }
}
