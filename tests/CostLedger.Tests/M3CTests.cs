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
/// M3-C 测试：对上vs对下、资金vs应付应收、人民币中文大写。
/// </summary>
public class M3CTests
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
    public async Task 对上vs对下_差额为毛利空间()
    {
        using var db = CreateSeededContext();
        var r = await new ReconciliationService(db, new CostSpineService(db)).UpDownVarianceAsync(1);

        r.Upstream.Should().Be(12_480_000m);   // MAIN-CZR-001
        r.Downstream.Should().Be(4_633_000m);  // 采购+分包×2+服务
        r.Diff.Should().Be(7_847_000m);
        r.Rows.Should().ContainSingle(x => x.IsUpstream && x.TypeCode == "Main");
    }

    [Fact]
    public async Task 资金vs应付应收_收付款缺口()
    {
        using var db = CreateSeededContext();
        var svc = new ReconciliationService(db, new CostSpineService(db));

        var purchase = db.Contracts.Single(c => c.Code == "PO-FAR-001");
        var main = db.Contracts.Single(c => c.Code == "MAIN-CZR-001");
        var partyId = purchase.PartyId;

        db.Payments.Add(new Payment { Code = "PM-1", ContractId = purchase.Id, PartyId = partyId, Direction = PaymentDirection.Pay, Amount = 1_000_000m, Date = new DateOnly(2026, 6, 1) });
        db.Payments.Add(new Payment { Code = "RC-1", ContractId = main.Id, PartyId = partyId, Direction = PaymentDirection.Receive, Amount = 2_000_000m, Date = new DateOnly(2026, 6, 5) });
        await db.SaveChangesAsync();

        var rows = await svc.FundReconAsync(1);

        var payable = rows.Single(x => x.Kind == "应付（对下）");
        payable.Should.Should().Be(4_670_000m);      // 已发生成本
        payable.Actual.Should().Be(1_000_000m);      // 已付
        payable.Gap.Should().Be(3_670_000m);

        var receivable = rows.Single(x => x.Kind == "应收（对上）");
        receivable.Should.Should().Be(12_480_000m);
        receivable.Actual.Should().Be(2_000_000m);   // 已收
        receivable.Gap.Should().Be(10_480_000m);
    }

    [Theory]
    [InlineData(0, "零元整")]
    [InlineData(1863000, "壹佰捌拾陆万叁仟元整")]
    [InlineData(1248000, "壹佰贰拾肆万捌仟元整")]
    [InlineData(1000000, "壹佰万元整")]
    [InlineData(12000000, "壹仟贰佰万元整")]
    [InlineData(100000000, "壹亿元整")]
    [InlineData(1234.56, "壹仟贰佰叁拾肆元伍角陆分")]
    public void 中文金额大写(decimal amount, string expected)
    {
        MoneyUtil.ToChineseCapital(amount).Should().Be(expected);
    }
}
