using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// M3-B 测试：六类口径（已完未结 / 预计最终）与对账引擎（合同vs成本、三算对比）。
/// </summary>
public class M3BTests
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
    public async Task 六类口径_已完未结与预计最终()
    {
        using var db = CreateSeededContext();
        var catId = db.CostCategories.Single(c => c.Code == "M-CABLE").Id;

        // 追加一个"已签认但未回写"的计量 → 形成已完未结
        var m = new Measurement { Code = "MS-1", ProjectId = 1, IsSigned = true, Date = new DateOnly(2026, 5, 1) };
        m.Lines.Add(new MeasurementLine { CostCategoryId = catId, ItemName = "电缆", Amount = 100_000m });
        db.Measurements.Add(m);
        await db.SaveChangesAsync();

        var cable = (await new CostSpineService(db).GetSpineAsync(1)).Single(l => l.CostCategoryName == "电力电缆");

        cable.WorkDoneNotSettled.Should().Be(100_000m);
        // EAC = max(已承诺 1,863,000, 已发生 2,000,000 + 已完未结 100,000) = 2,100,000
        cable.EstimatedFinal.Should().Be(2_100_000m);
    }

    [Fact]
    public async Task 对账_合同vs成本_电缆超合同额()
    {
        using var db = CreateSeededContext();
        var rows = await new ReconciliationService(db, new CostSpineService(db)).ContractVsCostAsync(1);

        rows.Should().HaveCount(4);
        var cable = rows.Single(r => r.ContractCode == "PO-FAR-001");
        cable.ContractAmount.Should().Be(1_863_000m);
        cable.Incurred.Should().Be(2_000_000m);
        cable.Diff.Should().Be(137_000m);
        cable.IsBreach.Should().BeTrue();
    }

    [Fact]
    public async Task 对账_三算对比_目标实际与结算()
    {
        using var db = CreateSeededContext();
        var s = await new ReconciliationService(db, new CostSpineService(db)).ThreeWaySummaryAsync(1);

        s.Target.Should().Be(4_633_000m);      // 1,863,000 + 2,270,000 + 500,000
        s.Incurred.Should().Be(4_670_000m);    // 2,000,000 + 2,270,000 + 400,000
        s.Settled.Should().Be(0m);             // 尚未结算
        s.TargetVsIncurred.Should().Be(37_000m);
    }
}
