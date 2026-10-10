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
/// UI 垂直切片：按三个新视图（项目经济驾驶舱 / 成本控制项与归属 / 成本预测与变化事项）的调用顺序，
/// 用同一套服务把一个项目的经济链路跑通一遍。它不是新的领域规则，而是保证界面所依赖的读写在真实顺序下成立。
/// </summary>
public class VerticalSliceTests
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
    public async Task 驾驶舱在没有预测版本时不得给出预计最终成本()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.ExternalAmountFacts.Add(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure, Amount = 1_000_000m,
            AsOfDate = new DateOnly(2026, 10, 10), Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.ContractPackage, Source = "采购台账"
        });
        await db.SaveChangesAsync();

        // 驾驶舱在无版本时拿到 null：UI 据此显示"尚无预测版本"，绝不用已知支出冒充。
        var summary = await new CostForecastService(db).GetSummaryAsync(project.Id);
        summary.Should().BeNull();

        var economy = await new EconomicFactsService(db).SummarizeProjectAsync(project.Id);
        economy.KnownExpenditure.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task 控制项_关系_外部金额_分配_预测_冻结端到端跑通()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "10kV外线" };
        var party = new Party { Name = "远东电缆", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();

        var factsSvc = new EconomicFactsService(db);
        var forecastSvc = new CostForecastService(db);

        // 1) 控制项
        var elec = new CostControlItem { ProjectId = project.Id, Code = "01", Name = "高压电缆材料", SortOrder = 1 };
        var civil = new CostControlItem { ProjectId = project.Id, Code = "02", Name = "排管土建", SortOrder = 2 };
        db.AddRange(elec, civil);
        await db.SaveChangesAsync();

        // 2) 业务关系（角色/板块/方向必须过领域规则）
        await factsSvc.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id, RoleCode = PartyRoleCodes.MaterialSupplier,
            Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure
        });

        // 3) 外部金额事实（挂单位 → 方向沿用关系）
        var fact = await factsSvc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, PartyId = party.Id, Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Neutral, // 板块允许中性；挂关系后方向应沿用关系（支出）
            Amount = 1_600_000m, AsOfDate = new DateOnly(2026, 10, 6),
            Status = ExternalFactStatus.ExternallyProvided, Precision = ManagementPrecision.ContractPackage,
            Source = "变压器采购合同HT-2026-19", ProvidedBy = "物资部王工"
        });
        fact.Direction.Should().Be(EconomicDirection.Expenditure);

        // 4) 一笔来源拆到两个控制项
        await factsSvc.AllocateAsync(fact.Id, elec.Id, 1_200_000m, ManagementPrecision.ContractPackage, AllocationStatus.Allocated);
        await factsSvc.AllocateAsync(fact.Id, civil.Id, 400_000m, ManagementPrecision.Professional, AllocationStatus.Estimated);

        var items = await factsSvc.SummarizeControlItemsAsync(project.Id);
        items.Should().HaveCount(2);
        items.Single(x => x.CostControlItemId == elec.Id).AllocatedAmount.Should().Be(1_200_000m);
        items.Single(x => x.CostControlItemId == civil.Id).EstimatedAmount.Should().Be(400_000m);

        // 5) 建预测版本、录行、冻结
        var version = await forecastSvc.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 10), ForecastConfidence.High, "电缆按10月价平");
        await forecastSvc.UpsertLineAsync(version.Id, elec.Id, 1_500_000m, 0m, 100_000m);
        await forecastSvc.UpsertLineAsync(version.Id, civil.Id, 400_000m, 0m, 0m);
        await forecastSvc.FreezeAsync(version.Id);

        var summary = await forecastSvc.GetSummaryAsync(project.Id);
        summary.Should().NotBeNull();
        summary!.Status.Should().Be(ForecastVersionStatus.Frozen);
        // 已知金额来自分配：1,200,000 + 400,000 = 1,600,000
        summary.KnownTotal.Should().Be(1_600_000m);
        // 预计最终 = 已知 + 剩余 + 风险 = 1,600,000 + 0 + 100,000 = 1,700,000
        summary.EstimatedFinalTotal.Should().Be(1_700_000m);
        summary.VarianceTotal.Should().Be(1_700_000m - 1_900_000m);
        // 全部可分配金额都已归属 → 无未归属
        summary.UnallocatedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task 存在未归属金额时冻结把可信度上限压到中()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "01", Name = "控制项" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();

        var factsSvc = new EconomicFactsService(db);
        var forecastSvc = new CostForecastService(db);
        var fact = await factsSvc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure, Amount = 1_000_000m,
            AsOfDate = new DateOnly(2026, 10, 10), Status = ExternalFactStatus.Estimated,
            Precision = ManagementPrecision.Project, Source = "现场汇总"
        });
        // 只归属一部分 → 留 400,000 未归属
        await factsSvc.AllocateAsync(fact.Id, item.Id, 600_000m, ManagementPrecision.Project, AllocationStatus.Allocated);

        var version = await forecastSvc.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 10), ForecastConfidence.High, "假设");
        await forecastSvc.UpsertLineAsync(version.Id, item.Id, 800_000m, 0m, 0m);
        await forecastSvc.FreezeAsync(version.Id);

        (await forecastSvc.GetLatestVersionAsync(project.Id))!.Confidence.Should().Be(ForecastConfidence.Medium);
    }

    [Fact]
    public async Task 新建控制项_编号或名称空则拒绝_重复编号则拒绝()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var svc = new EconomicFactsService(db);

        // 名称空 → 拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateControlItemAsync(project.Id, "01", "  "));
        // 编号空 → 拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateControlItemAsync(project.Id, "", "材料"));

        // 正常新建 → 落库
        var item = await svc.CreateControlItemAsync(project.Id, "01", "高压电缆材料", 10);
        item.Id.Should().BeGreaterThan(0);
        (await db.CostControlItems.CountAsync(x => x.ProjectId == project.Id)).Should().Be(1);

        // 重复编号 → 拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateControlItemAsync(project.Id, "01", "另一项"));
    }

    [Fact]
    public async Task 分配更正_追加反向冲销行_原分配不被改写()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "01", Name = "控制项" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();

        var svc = new EconomicFactsService(db);
        var fact = await svc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure,
            Amount = 1_000_000m, AsOfDate = new DateOnly(2026, 10, 10), Status = ExternalFactStatus.Checked,
            Precision = ManagementPrecision.ContractPackage, Source = "来源"
        });
        var alloc = await svc.AllocateAsync(fact.Id, item.Id, 400_000m, ManagementPrecision.ContractPackage, AllocationStatus.Allocated);

        // 更正：追加等额负数冲销
        var rev = await svc.ReverseAllocationAsync(alloc.Id, "录错控制项");

        // 原分配行保持不变（未被删除/改写）
        var original = await db.ManagementAllocations.AsNoTracking().SingleAsync(x => x.Id == alloc.Id);
        original.Amount.Should().Be(400_000m);
        rev.Amount.Should().Be(-400_000m);
        rev.ReversedAllocationId.Should().Be(alloc.Id);

        // 汇总净额归零
        var items = await svc.SummarizeControlItemsAsync(project.Id);
        items.Single().AllocatedAmount.Should().Be(0m);

        // 冲销后可重新分配（来源额度被释放）
        await svc.AllocateAsync(fact.Id, item.Id, 400_000m, ManagementPrecision.ContractPackage, AllocationStatus.Allocated);
        (await svc.SummarizeControlItemsAsync(project.Id)).Single().AllocatedAmount.Should().Be(400_000m);
    }

    [Fact]
    public async Task 分配更正_不能重复冲销_冲销行不能再冲销()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "01", Name = "控制项" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();

        var svc = new EconomicFactsService(db);
        var fact = await svc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure,
            Amount = 1_000_000m, AsOfDate = new DateOnly(2026, 10, 10), Status = ExternalFactStatus.Checked,
            Precision = ManagementPrecision.ContractPackage, Source = "来源"
        });
        var alloc = await svc.AllocateAsync(fact.Id, item.Id, 400_000m, ManagementPrecision.ContractPackage, AllocationStatus.Allocated);
        var rev = await svc.ReverseAllocationAsync(alloc.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReverseAllocationAsync(alloc.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReverseAllocationAsync(rev.Id));
    }

    [Fact]
    public async Task 公司汇总等于各项目逐列相加且无版本项目不计入预计最终()
    {
        using var db = CreateContext();
        var p1 = new Project { Code = "P-1", Name = "项目一" };
        var p2 = new Project { Code = "P-2", Name = "项目二" };
        db.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var factsSvc = new EconomicFactsService(db);
        var forecastSvc = new CostForecastService(db);

        var i1 = new CostControlItem { ProjectId = p1.Id, Code = "01", Name = "控制项A" };
        var i2 = new CostControlItem { ProjectId = p2.Id, Code = "01", Name = "控制项B" };
        db.AddRange(i1, i2);
        await db.SaveChangesAsync();

        // P-1：归属 100 万 + 有冻结预测
        var f1 = await factsSvc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = p1.Id, Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure,
            Amount = 1_000_000m, AsOfDate = new DateOnly(2026, 10, 10), Status = ExternalFactStatus.Checked,
            Precision = ManagementPrecision.ContractPackage, Source = "P1来源"
        });
        await factsSvc.AllocateAsync(f1.Id, i1.Id, 1_000_000m, ManagementPrecision.ContractPackage, AllocationStatus.Allocated);
        var v1 = await forecastSvc.CreateVersionAsync(p1.Id, new DateOnly(2026, 10, 10), ForecastConfidence.Medium, "假设1");
        await forecastSvc.UpsertLineAsync(v1.Id, i1.Id, 1_200_000m, 0m, 0m);
        await forecastSvc.FreezeAsync(v1.Id);

        // P-2：只有来源、无预测版本 → 不计入公司预计最终
        var f2 = await factsSvc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = p2.Id, Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure,
            Amount = 500_000m, AsOfDate = new DateOnly(2026, 10, 9), Status = ExternalFactStatus.Estimated,
            Precision = ManagementPrecision.Project, Source = "P2来源"
        });
        await factsSvc.AllocateAsync(f2.Id, i2.Id, 500_000m, ManagementPrecision.Project, AllocationStatus.Allocated);

        var company = await new CompanyService(db).SummarizeAsync();

        company.ProjectCount.Should().Be(2);
        company.MissingForecastCount.Should().Be(1);
        company.KnownExpenditure.Should().Be(1_500_000m);       // 100万 + 50万
        company.EstimatedFinal.Should().Be(1_000_000m);         // 只有 P-1：已知 100万 + 剩余 0 + 风险 0
        company.TargetTotal.Should().Be(1_200_000m);            // 仅 P-1 的目标
        company.UnallocatedAmount.Should().Be(0m);
        company.Projects.Single(p => p.Code == "P-2").EstimatedFinal.Should().BeNull();
    }
}
