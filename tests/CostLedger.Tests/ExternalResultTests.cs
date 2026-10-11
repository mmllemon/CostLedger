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

/// <summary>P4 外部成果结果：报送/确认两金额独立，差额为确认−报送；只登记结果不生成成果文件。</summary>
public class ExternalResultTests
{
    private static AppDbContext CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task 结算对预测差异_三口径并列且无预测时成本侧为空()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var svc = new ExternalResultService(db);

        // 无外部成果、无预测：成本侧为 null，列不报错。
        var empty = await svc.SummarizeVarianceAsync(project.Id);
        empty.HasForecast.Should().BeFalse();
        empty.EstimatedFinalCost.Should().BeNull();

        // 登记送审 900,000 / 审定确认 850,000
        await svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = project.Id, Period = "竣工结算", Type = ResultRecordType.ApprovedSettlement,
            SubmittedAmount = 900_000m, ConfirmedAmount = 850_000m,
            AsOfDate = new DateOnly(2026, 10, 1), Source = "结算审定单"
        });

        // 对上收入事实 880,000 → 预计收入
        await new EconomicFactsService(db).RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.UpstreamRevenue, Direction = EconomicDirection.Income,
            Amount = 880_000m, AsOfDate = new DateOnly(2026, 9, 30), Status = ExternalFactStatus.Checked,
            Precision = ManagementPrecision.Project, Source = "总包合同"
        });

        var v = await svc.SummarizeVarianceAsync(project.Id);
        v.SubmittedRevenue.Should().Be(900_000m);
        v.ConfirmedRevenue.Should().Be(850_000m);
        v.ExpectedIncome.Should().Be(880_000m);
        // 有审定时，确认收入取审定 850,000
        v.RevenueVarianceVsSubmitted.Should().Be(850_000m - 900_000m);
        v.RevenueVarianceVsExpected.Should().Be(850_000m - 880_000m);
    }

    [Fact]
    public async Task 结算清单可选对齐_未对齐允许且汇总区分对齐与未对齐()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "01", Name = "电缆敷设劳务" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();

        var svc = new ExternalResultService(db);
        var rec = await svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = project.Id, Period = "竣工结算", Type = ResultRecordType.ApprovedSettlement,
            SubmittedAmount = 1000m, ConfirmedAmount = 900m,
            AsOfDate = new DateOnly(2026, 10, 1), Source = "结算审定单"
        });

        // 一行对齐、一行未对齐（不挂控制项，允许）
        await svc.AddLineItemAsync(rec.Id, "电缆敷设", 600m, item.Id);
        await svc.AddLineItemAsync(rec.Id, "未归类零星", 300m, null);

        var a = await svc.SummarizeAlignmentAsync(project.Id);
        a.LineCount.Should().Be(2);
        a.AlignedLineCount.Should().Be(1);
        a.AlignedAmount.Should().Be(600m);
        a.UnalignedAmount.Should().Be(300m);
        a.TotalAmount.Should().Be(900m);
        a.AlignedRate.Should().Be(600m / 900m);
    }

    [Fact]
    public async Task 结算清单行_名称为空被拒_跨项目控制项被拒()
    {
        using var db = CreateContext();
        var p1 = new Project { Code = "P-1", Name = "项目一" };
        var p2 = new Project { Code = "P-2", Name = "项目二" };
        db.AddRange(p1, p2);
        await db.SaveChangesAsync();
        var otherItem = new CostControlItem { ProjectId = p2.Id, Code = "01", Name = "他项目控制项" };
        db.CostControlItems.Add(otherItem);
        await db.SaveChangesAsync();

        var svc = new ExternalResultService(db);
        var rec = await svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = p1.Id, Period = "P", Type = ResultRecordType.ProgressMeasurement,
            SubmittedAmount = 1m, ConfirmedAmount = 1m, AsOfDate = new DateOnly(2026, 1, 1), Source = "S"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AddLineItemAsync(rec.Id, "  ", 100m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AddLineItemAsync(rec.Id, "跨项目", 100m, otherItem.Id));
    }

    [Fact]
    public async Task 外部成果结果_两金额独立_差额为确认减报送()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var svc = new ExternalResultService(db);

        await svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = project.Id, Period = "2026-09 进度款", Type = ResultRecordType.ProgressMeasurement,
            SubmittedAmount = 850_000m, ConfirmedAmount = 798_000m,
            AsOfDate = new DateOnly(2026, 9, 30), Source = "进度款报量表 2026-09"
        });

        var s = await svc.SummarizeAsync(project.Id);
        s.SubmittedTotal.Should().Be(850_000m);
        s.ConfirmedTotal.Should().Be(798_000m);
        s.VarianceTotal.Should().Be(-52_000m);   // 确认 − 报送，负数=报送高于确认，需关注
        s.Lines.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("", "来源", "2026-09-30", "期次")]
    [InlineData("期次", "", "2026-09-30", "来源")]
    public async Task 外部成果结果_缺少期次或来源被拒(string period, string source, string asOf, string keyword)
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var svc = new ExternalResultService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = project.Id, Period = period, Source = source,
            AsOfDate = DateOnly.Parse(asOf), SubmittedAmount = 1m, ConfirmedAmount = 1m
        }));
        ex.Message.Should().Contain(keyword);
    }

    [Fact]
    public async Task 外部成果结果_负金额被拒()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var svc = new ExternalResultService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RegisterAsync(new ExternalResultRecord
        {
            ProjectId = project.Id, Period = "P", Source = "S", AsOfDate = new DateOnly(2026, 1, 1),
            SubmittedAmount = -1m, ConfirmedAmount = 0m
        }));
        ex.Message.Should().Contain("不能为负");
    }
}
