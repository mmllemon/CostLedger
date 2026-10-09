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

/// <summary>往来单位台账测试：跨项目汇总 合同额 / 已付 / 未付。</summary>
public class PartyLedgerTests
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
    public async Task 往来单位_跨项目汇总合同与付款()
    {
        using var db = CreateSeededContext();
        var supplier = db.Parties.Single(p => p.Name == "远东电缆");

        // 第二个项目，同样与远东签合同 + 一笔付款
        var p2 = new Project { Code = "P2", Name = "另一个项目" };
        db.Projects.Add(p2);
        await db.SaveChangesAsync();

        var c2 = new Contract { Code = "PO-FAR-002", ProjectId = p2.Id, PartyId = supplier.Id, TypeCode = "Purchase", Amount = 500_000m, TaxRate = 0.13m, SignDate = new DateOnly(2026, 1, 1) };
        db.Contracts.Add(c2);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment { Code = "PM-2", ContractId = c2.Id, PartyId = supplier.Id, Direction = PaymentDirection.Pay, Type = PaymentType.Progress, Amount = 300_000m, Date = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();

        var l = await new PartyLedgerService(db).BuildAsync(supplier.Id);

        l.ContractCount.Should().Be(2);        // 两个项目各一份
        l.ProjectCount.Should().Be(2);
        l.ContractTotal.Should().Be(2_363_000m); // 1,863,000 + 500,000
        l.Paid.Should().Be(300_000m);
        l.Unpaid.Should().Be(2_063_000m);        // 合同总额 − 已付
        l.ByProject.Should().HaveCount(2);
    }
}
