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
/// M3-D 测试：资金台账（收款/付款）——累计、比例(以合同额为基数)、未收(付)。
/// 定位收敛（2026-10-09）：只做记录+累计，不做抵扣试算。
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
    public async Task 收款台账_累计_比例_未收()
    {
        using var db = CreateSeededContext();
        var main = db.Contracts.Single(c => c.Code == "MAIN-CZR-001");
        db.Payments.Add(new Payment { Code = "RC-1", ContractId = main.Id, PartyId = main.PartyId, Direction = PaymentDirection.Receive, Type = PaymentType.Progress, Amount = 5_000_000m, Date = new DateOnly(2026, 6, 1) });
        await db.SaveChangesAsync();

        var rows = await new PaymentService(db).FundLedgerAsync(1, PaymentDirection.Receive);
        var r = rows.Single(x => x.ContractCode == "MAIN-CZR-001");

        r.ContractAmount.Should().Be(12_480_000m);
        r.Cumulative.Should().Be(5_000_000m);
        r.Outstanding.Should().Be(7_480_000m);
        r.Ratio.Should().BeApproximately(5_000_000m / 12_480_000m, 0.0001m);
    }

    [Fact]
    public async Task 付款台账_累计与未付()
    {
        using var db = CreateSeededContext();
        var po = db.Contracts.Single(c => c.Code == "PO-FAR-001");
        db.Payments.Add(new Payment { Code = "PM-1", ContractId = po.Id, PartyId = po.PartyId, Direction = PaymentDirection.Pay, Type = PaymentType.Progress, Amount = 1_000_000m, Date = new DateOnly(2026, 6, 1) });
        await db.SaveChangesAsync();

        var rows = await new PaymentService(db).FundLedgerAsync(1, PaymentDirection.Pay);
        var r = rows.Single(x => x.ContractCode == "PO-FAR-001");

        r.ContractAmount.Should().Be(1_863_000m);
        r.Cumulative.Should().Be(1_000_000m);
        r.Outstanding.Should().Be(863_000m); // 未付
    }
}
