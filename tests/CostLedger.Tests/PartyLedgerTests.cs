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
        // 口径声明所依据的数据截至日期 = 本单位最晚业务日期（此处为 2026-02-01 的付款）
        l.AsOfDate.Should().Be(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task 往来单位_对上对下合同额分开且无业务日期时截至为空()
    {
        using var db = CreateSeededContext();
        var owner = db.Parties.Single(p => p.Name == "残疾人项目建设单位");

        var l = await new PartyLedgerService(db).BuildAsync(owner.Id);

        l.UpstreamAmount.Should().BeGreaterThan(0);
        l.DownstreamAmount.Should().Be(l.ContractTotal - l.UpstreamAmount);
        // 未收（推算）= 对上合同额 − 已登记收款；无业务日期时截至为空而非假造日期
        l.Unreceived.Should().Be(l.UpstreamAmount - l.Received);
        if (l.AsOfDate == null) l.ContractCount.Should().Be(0);
    }

    [Fact]
    public void 口径说明文本包含责任边界要素()
    {
        // 打印版式（A4 HTML）在 App 层（net9.0-windows，不被本测试工程引用）；
        // 这里只锁住口径声明必须包含的四类要素，防止日后被简化掉。
        var note = PartyLedgerCaliber.Build("2026-02-01");
        note.Should().Contain("数据截至：2026-02-01");
        note.Should().Contain("推算差额");
        note.Should().Contain("不等于财务应付/应收账款");
        note.Should().Contain("成本岗不对其完整性与合规性作出判断");
    }

    private static LedgerResult SampleLedger() => new(
        "往来单位台账 · 远东电缆",
        new[] { "项目", "合同额", "已付", "未付（推算）" },
        new[] { new[] { "P1", "1,863,000.00", "300,000.00", "1,563,000.00" } },
        new[] { new[] { "合计", "1,863,000.00", "300,000.00", "1,563,000.00" } });

    [Fact]
    public void A4版式_含口径声明与签章栏_未提供时不插入()
    {
        // 版式渲染已下沉 Infrastructure（A4ReportLayout），可直接单测：
        // 过去它藏在 net9.0-windows 的 App 层，任何打印要素都没有测试保护。
        var withNote = A4ReportLayout.Render("远东电缆", SampleLedger(), new DateTime(2026, 10, 10),
            company: "某建设集团", caliberNote: PartyLedgerCaliber.Build("2026-02-01"));
        var withoutNote = A4ReportLayout.Render("远东电缆", SampleLedger(), new DateTime(2026, 10, 10));

        withNote.Should().Contain("@page { size: A4")
            .And.Contain("某建设集团")
            .And.Contain("<tfoot>")
            .And.Contain("未付（推算）")
            .And.Contain("编制：").And.Contain("审核：")
            .And.Contain("class=\"note\"").And.Contain("数据截至：2026-02-01");
        withoutNote.Should().NotContain("class=\"note\"")
            .And.NotContain("某建设集团");
    }

    [Fact]
    public void A4版式_单位名含特殊字符时被转义()
    {
        var ledger = new LedgerResult("台账 <script>alert(1)</script>",
            new[] { "项目" }, new[] { new[] { "A&B" } }, new List<string[]>());
        var html = A4ReportLayout.Render("P", ledger, new DateTime(2026, 10, 10));
        html.Should().NotContain("<script>")
            .And.Contain("&lt;script&gt;")
            .And.Contain("A&amp;B");
    }
}
