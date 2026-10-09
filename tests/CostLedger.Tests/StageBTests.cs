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

/// <summary>阶段 B 测试：CSV 往返 + 台账导入（变更签证 / 收款）。</summary>
public class StageBTests
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
    public void CSV_往返_含逗号引号换行()
    {
        var rows = new System.Collections.Generic.List<string[]>
        {
            new[] { "a,b", "c\"d", "e\nf" }
        };
        var csv = CsvUtil.Write(new[] { "x", "y", "z" }, rows);
        var parsed = CsvUtil.Parse(csv);

        parsed.Should().HaveCount(2);
        parsed[0].Should().Equal("x", "y", "z");
        parsed[1].Should().Equal("a,b", "c\"d", "e\nf");
    }

    [Fact]
    public async Task 导入变更签证_进入台账净额()
    {
        using var db = CreateSeededContext();
        var csv = "编号,类型,内容,金额(±),日期,关联合同,状态\n" +
                  "CO-I1,现场签证,增加电缆,100000,2026-03-01,PO-FAR-001,已确认\n" +
                  "CO-I2,设计变更,减少基础,-30000,2026-03-02,,草稿\n";

        var res = await new LedgerImportService(db).ImportAsync(1, LedgerKind.ChangeOrder, csv);
        res.Created.Should().Be(2);
        res.Skipped.Should().Be(0);

        var ledger = await new LedgerService(db).BuildAsync(1, LedgerKind.ChangeOrder);
        ledger.Footers[2][3].Replace(",", "").Should().Be("70000.00"); // 净额
    }

    [Fact]
    public async Task 导入收款_方向为收款()
    {
        using var db = CreateSeededContext();
        var csv = "日期,编号,合同,往来单位,类型,金额\n" +
                  "2026-06-01,RC-I1,MAIN-CZR-001,残疾人项目建设单位,进度款,5000000\n";

        var res = await new LedgerImportService(db).ImportAsync(1, LedgerKind.Receipt, csv);
        res.Created.Should().Be(1);

        var p = db.Payments.Single();
        p.Direction.Should().Be(PaymentDirection.Receive);
        p.Amount.Should().Be(5_000_000m);
        p.ContractId.Should().NotBeNull();
    }

    [Fact]
    public async Task 导入_日期非法_跳过并报错()
    {
        using var db = CreateSeededContext();
        var csv = "编号,类型,内容,金额(±),日期,关联合同,状态\n" +
                  "CO-BAD,设计变更,坏日期,1000,not-a-date,,\n";

        var res = await new LedgerImportService(db).ImportAsync(1, LedgerKind.ChangeOrder, csv);
        res.Created.Should().Be(0);
        res.Skipped.Should().Be(1);
        res.Errors.Should().HaveCount(1);
    }
}
