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
/// M3-D 测试：付款试算器（质保金扣留 / 预付款抵扣 / 净付）与预付款余额。
/// </summary>
public class M3DTests
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
    public void 试算_按比例抵扣()
    {
        // 质保 5% → 5万；预付款比例 10% → 基 95万×10% = 9.5万（不超过余额 20万）
        var b = PaymentCalculator.Compute(1_000_000m, 0.05m, 0.10m, 200_000m, "按比例抵扣");
        b.Retention.Should().Be(50_000m);
        b.AdvanceDeduction.Should().Be(95_000m);
        b.Net.Should().Be(855_000m);
    }

    [Fact]
    public void 试算_一次性抵扣()
    {
        var b = PaymentCalculator.Compute(1_000_000m, 0.05m, 0.10m, 200_000m, "一次性抵扣");
        b.Retention.Should().Be(50_000m);
        b.AdvanceDeduction.Should().Be(200_000m);  // 扣完未抵余额
        b.Net.Should().Be(750_000m);
    }

    [Fact]
    public void 试算_无预付款余额则不抵扣()
    {
        var b = PaymentCalculator.Compute(1_000_000m, 0.05m, 0.10m, 0m, "按比例抵扣");
        b.AdvanceDeduction.Should().Be(0m);
        b.Net.Should().Be(950_000m);
    }

    [Fact]
    public async Task 预付款余额_扣除已抵扣()
    {
        using var db = CreateSeededContext();
        var svc = new PaymentService(db);
        var contract = db.Contracts.Single(c => c.Code == "PO-FAR-001");

        db.Payments.Add(new Payment { Code = "ADV-1", ContractId = contract.Id, PartyId = contract.PartyId, Direction = PaymentDirection.Pay, Type = PaymentType.Advance, Amount = 200_000m, Date = new DateOnly(2026, 1, 1) });
        db.Payments.Add(new Payment { Code = "PRG-1", ContractId = contract.Id, PartyId = contract.PartyId, Direction = PaymentDirection.Pay, Type = PaymentType.Progress, Amount = 855_000m, AdvanceDeduction = 95_000m, Date = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();

        (await svc.AdvanceOutstandingAsync(contract.Id)).Should().Be(105_000m); // 20万 − 9.5万
    }

    [Fact]
    public async Task 按合同参数试算()
    {
        using var db = CreateSeededContext();
        var contract = db.Contracts.Single(c => c.Code == "PO-FAR-001");
        contract.RetentionRate = 0.05m;
        contract.AdvanceRate = 0.10m;
        contract.AdvanceDeductionMethod = "按比例抵扣";
        db.Payments.Add(new Payment { Code = "ADV-1", ContractId = contract.Id, PartyId = contract.PartyId, Direction = PaymentDirection.Pay, Type = PaymentType.Advance, Amount = 200_000m, Date = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();

        var b = await new PaymentService(db).ComputeAsync(contract.Id, 1_000_000m);
        b.Net.Should().Be(855_000m);
    }
}
