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
/// 成本预测版本与变化事项测试。核心边界：预测是成本人员的管理判断，
/// 版本冻结后不可改写；四个变化金额相互独立，成本岗不能把自己的估值写成对方已认可。
/// </summary>
public class ForecastTests
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

    private static async Task<(Project project, CostControlItem elec, CostControlItem civil)> NewProjectAsync(AppDbContext db)
    {
        var project = new Project { Code = "P-1", Name = "配电工程" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var elec = new CostControlItem { ProjectId = project.Id, Code = "ELEC", Name = "电气工程", SortOrder = 1 };
        var civil = new CostControlItem { ProjectId = project.Id, Code = "CIVIL", Name = "土建", SortOrder = 2 };
        db.AddRange(elec, civil);
        await db.SaveChangesAsync();
        return (project, elec, civil);
    }

    private static ExternalAmountFact Fact(int projectId, decimal amount, BusinessSegment segment = BusinessSegment.MaterialSupply) => new()
    {
        ProjectId = projectId,
        Segment = segment,
        Direction = EconomicDirection.Expenditure,
        Amount = amount,
        AsOfDate = new DateOnly(2026, 10, 10),
        Status = ExternalFactStatus.ExternallyProvided,
        Precision = ManagementPrecision.Professional,
        Source = "采购包月汇总"
    };

    [Fact]
    public async Task 已知金额来自分配关系且不接受手填()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var facts = new EconomicFactsService(db);
        var forecast = new CostForecastService(db);

        var material = await facts.RegisterFactAsync(Fact(project.Id, 1_000_000m));
        await facts.AllocateAsync(material.Id, elec.Id, 600_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);

        var version = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 10), ForecastConfidence.Medium);
        var line = await forecast.UpsertLineAsync(version.Id, elec.Id, 900_000m, 200_000m, 50_000m);

        line.KnownAmount.Should().Be(600_000m);
        line.EstimatedFinal.Should().Be(850_000m);
        line.PrecisionCap.Should().Be(ManagementPrecision.Professional);
        line.IsOverTarget.Should().BeFalse();

        // 后来的分配自动进入草稿版本，无需人工重录
        var extra = await facts.RegisterFactAsync(Fact(project.Id, 400_000m, BusinessSegment.LaborSubcontract));
        await facts.AllocateAsync(extra.Id, elec.Id, 400_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);
        var summary = await forecast.GetSummaryAsync(project.Id, version.Id);
        summary!.KnownTotal.Should().Be(1_000_000m);
        summary.EstimatedFinalTotal.Should().Be(1_250_000m);
    }

    [Fact]
    public async Task 冻结后不能被改写_修订必须新建版本()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var facts = new EconomicFactsService(db);
        var forecast = new CostForecastService(db);
        var material = await facts.RegisterFactAsync(Fact(project.Id, 1_000_000m));
        await facts.AllocateAsync(material.Id, elec.Id, 1_000_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);

        var v1 = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 9, 30), ForecastConfidence.Medium, "按 9 月价格水平，剩余工程量按现场形象进度");
        await forecast.UpsertLineAsync(v1.Id, elec.Id, 1_200_000m, 100_000m, 0m);
        await forecast.FreezeAsync(v1.Id);

        var editDraft = () => forecast.UpsertLineAsync(v1.Id, elec.Id, 2m, 2m, 2m);
        await editDraft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已冻结*新建版本*");
        var refreeze = () => forecast.FreezeAsync(v1.Id);
        await refreeze.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已冻结*");

        var v2 = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 31), ForecastConfidence.High, "电缆涨价 6% 已计入剩余估计");
        await forecast.UpsertLineAsync(v2.Id, elec.Id, 1_200_000m, 180_000m, 60_000m);

        v1.VersionNumber.Should().Be(1);
        v2.VersionNumber.Should().Be(2);
        (await db.CostForecastLines.Where(x => x.CostForecastVersionId == v1.Id).SingleAsync()).KnownAmount
            .Should().Be(1_000_000m);
    }

    [Fact]
    public async Task 冻结版本保留当时快照_不被后续数据改写()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var facts = new EconomicFactsService(db);
        var forecast = new CostForecastService(db);
        var material = await facts.RegisterFactAsync(Fact(project.Id, 1_000_000m));
        await facts.AllocateAsync(material.Id, elec.Id, 400_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);

        var v1 = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 9, 30), ForecastConfidence.High, "9 月价格水平");
        await forecast.UpsertLineAsync(v1.Id, elec.Id, 1_000_000m, 500_000m, 0m);
        await forecast.FreezeAsync(v1.Id);

        // 10 月又归集了 600,000 到同一控制项
        var oct = await facts.RegisterFactAsync(Fact(project.Id, 600_000m, BusinessSegment.ProjectOverhead));
        await facts.AllocateAsync(oct.Id, elec.Id, 600_000m, ManagementPrecision.Project, AllocationStatus.Allocated);

        var frozen = await forecast.GetSummaryAsync(project.Id, v1.Id);

        // 冻结版本的历史结论不被后来数据漂移
        frozen!.KnownTotal.Should().Be(400_000m);
        frozen.Status.Should().Be(ForecastVersionStatus.Frozen);
        // 新草稿版本才反映当前数据
        var draft = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 31), ForecastConfidence.High, "10 月价格水平");
        await forecast.UpsertLineAsync(draft.Id, elec.Id, 1_000_000m, 500_000m, 0m);
        (await forecast.GetSummaryAsync(project.Id, draft.Id))!.KnownTotal.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task 缺少假设或预测行时拒绝冻结()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var forecast = new CostForecastService(db);

        var empty = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 1), ForecastConfidence.Low);
        Func<Task> freezeEmpty = () => forecast.FreezeAsync(empty.Id);
        await freezeEmpty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*没有任何控制项预测行*");

        var noBasis = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 1), ForecastConfidence.Low);
        await forecast.UpsertLineAsync(noBasis.Id, elec.Id, 100m, 0m, 0m);
        Func<Task> freezeNoBasis = () => forecast.FreezeAsync(noBasis.Id);
        await freezeNoBasis.Should().ThrowAsync<InvalidOperationException>().WithMessage("*必须写明编制假设*");
    }

    [Fact]
    public async Task 存在未归属金额时压低预测可信度上限()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var facts = new EconomicFactsService(db);
        var forecast = new CostForecastService(db);

        // 900,000 全部未分配
        var unallocated = await facts.RegisterFactAsync(Fact(project.Id, 900_000m, BusinessSegment.SpecialistSubcontract));
        unallocated.Id.Should().BeGreaterThan(0);

        var version = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 1), ForecastConfidence.High, "假定其余金额按专业水平发生");
        await forecast.UpsertLineAsync(version.Id, elec.Id, 1_000_000m, 0m, 0m);
        var frozen = await forecast.FreezeAsync(version.Id);

        frozen.Confidence.Should().Be(ForecastConfidence.Medium);
        (await forecast.GetSummaryAsync(project.Id, version.Id))!.UnallocatedAmount.Should().Be(900_000m);
    }

    [Fact]
    public async Task 变化事项四个金额相互独立且不被覆盖()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var forecast = new CostForecastService(db);

        var item = await forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = project.Id, Code = "V-023", Title = "配电间调整",
            Segment = BusinessSegment.UpstreamRevenue, CostControlItemId = elec.Id,
            CostOfficerEstimate = 120_000m, CounterpartySubmitted = 135_000m,
            IncludedInForecast = 120_000m, Status = ChangeItemStatus.Assessed,
            AsOfDate = new DateOnly(2026, 10, 10), Source = "变更通知 2026-09", ProvidedBy = "项目部"
        });

        item.CostOfficerEstimate.Should().Be(120_000m);
        item.CounterpartySubmitted.Should().Be(135_000m);
        item.ExternallyConfirmed.Should().BeNull();

        // 对方确认后，成本人员的测算与纳入金额仍保持原值，只有确认金额新增
        item.ExternallyConfirmed = 130_000m;
        item.Status = ChangeItemStatus.Confirmed;
        var updated = await forecast.UpdateChangeItemAsync(item);
        updated.ExternallyConfirmed.Should().Be(130_000m);
        updated.CostOfficerEstimate.Should().Be(120_000m);
        updated.CounterpartySubmitted.Should().Be(135_000m);
        updated.IncludedInForecast.Should().Be(120_000m);
    }

    // -1 表示"未填写"（InlineData 不支持可空值类型的 null）。
    [Theory]
    [InlineData(ChangeItemStatus.Unassessed, -1, -1, -1, -1, true)]
    [InlineData(ChangeItemStatus.Unassessed, -1, -1, -1, 100, false)]
    [InlineData(ChangeItemStatus.Assessed, -1, -1, -1, -1, false)]
    [InlineData(ChangeItemStatus.Assessed, 100, -1, -1, -1, true)]
    [InlineData(ChangeItemStatus.Assessed, 100, -1, -1, 100, true)]
    [InlineData(ChangeItemStatus.Submitted, 100, -1, -1, 100, false)]
    [InlineData(ChangeItemStatus.Submitted, 100, 130, -1, 100, true)]
    [InlineData(ChangeItemStatus.ExternalFeedback, 100, 130, -1, 100, true)]
    [InlineData(ChangeItemStatus.ExternalFeedback, 100, 130, -1, -1, true)]
    [InlineData(ChangeItemStatus.Confirmed, 100, 130, -1, 100, false)]
    [InlineData(ChangeItemStatus.Confirmed, 100, 130, 125, 100, true)]
    [InlineData(ChangeItemStatus.NotIncluded, 100, -1, -1, -1, true)]
    [InlineData(ChangeItemStatus.NotIncluded, 100, -1, -1, 100, false)]
    [InlineData(ChangeItemStatus.Assessed, 100, -1, -1, 0, false)]
    public void 状态决定哪些金额必须存在(
        ChangeItemStatus status, int estimate, int submitted, int confirmed, int included, bool valid)
    {
        static decimal? Value(int v) => v == -1 ? null : v;
        var item = new ChangeItem
        {
            Code = "V-1", Title = "测试事项", Segment = BusinessSegment.UpstreamRevenue,
            AsOfDate = new DateOnly(2026, 10, 10), Source = "变更通知",
            Status = status, CostOfficerEstimate = Value(estimate),
            CounterpartySubmitted = Value(submitted), ExternallyConfirmed = Value(confirmed),
            IncludedInForecast = Value(included)
        };

        var violation = ForecastRules.FindViolation(item);
        if (valid) violation.Should().BeNull();
        else violation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task 未纳入预测的变化金额单独提示而不是静默漏掉()
    {
        using var db = CreateContext();
        var (project, elec, civil) = await NewProjectAsync(db);
        var forecast = new CostForecastService(db);

        await forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = project.Id, Code = "V-001", Title = "已测算已纳入",
            Segment = BusinessSegment.MaterialSupply, CostControlItemId = elec.Id,
            CostOfficerEstimate = 120_000m, IncludedInForecast = 120_000m,
            Status = ChangeItemStatus.Assessed, AsOfDate = new DateOnly(2026, 10, 1), Source = "变更通知"
        });
        await forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = project.Id, Code = "V-002", Title = "已测算但暂未纳入",
            Segment = BusinessSegment.MaterialSupply, CostControlItemId = civil.Id,
            CostOfficerEstimate = 80_000m, Status = ChangeItemStatus.Assessed,
            AsOfDate = new DateOnly(2026, 10, 2), Source = "现场签证台账"
        });
        await forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = project.Id, Code = "V-003", Title = "明确不纳入",
            Segment = BusinessSegment.MaterialSupply, CostControlItemId = civil.Id,
            CostOfficerEstimate = 500_000m, Status = ChangeItemStatus.NotIncluded,
            AsOfDate = new DateOnly(2026, 10, 3), Source = "口头事项"
        });

        var version = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 10), ForecastConfidence.Medium, "已含 V-001，V-002 待复测");
        await forecast.UpsertLineAsync(version.Id, elec.Id, 1_000_000m, 120_000m, 0m);
        await forecast.UpsertLineAsync(version.Id, civil.Id, 500_000m, 0m, 0m);

        var summary = await forecast.GetSummaryAsync(project.Id, version.Id);

        summary!.UnincludedChangeAmount.Should().Be(80_000m);
        summary.PendingConfirmationChangeAmount.Should().Be(120_000m);
        summary.Lines.Single(x => x.Code == "CIVIL").UnincludedChangeAmount.Should().Be(80_000m);
        summary.Lines.Single(x => x.Code == "ELEC").PendingConfirmationChangeAmount.Should().Be(120_000m);
        summary.ActiveControlItemCount.Should().Be(2);
        // 无分配金额时：电气 预计最终 120,000 对目标 1,000,000；土建 0 对 500,000
        summary.TargetTotal.Should().Be(1_500_000m);
        summary.EstimatedFinalTotal.Should().Be(120_000m);
        summary.VarianceTotal.Should().Be(120_000m - 1_500_000m);
    }

    [Fact]
    public async Task 与上一冻结版本比较显示预测变化额()
    {
        using var db = CreateContext();
        var (project, elec, _) = await NewProjectAsync(db);
        var facts = new EconomicFactsService(db);
        var forecast = new CostForecastService(db);
        var material = await facts.RegisterFactAsync(Fact(project.Id, 1_000_000m));
        await facts.AllocateAsync(material.Id, elec.Id, 1_000_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);

        var v1 = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 9, 30), ForecastConfidence.High, "9 月口径");
        await forecast.UpsertLineAsync(v1.Id, elec.Id, 1_100_000m, 50_000m, 0m);
        await forecast.FreezeAsync(v1.Id);

        var v2 = await forecast.CreateVersionAsync(project.Id, new DateOnly(2026, 10, 31), ForecastConfidence.High, "10 月电缆涨价");
        await forecast.UpsertLineAsync(v2.Id, elec.Id, 1_100_000m, 150_000m, 0m);
        var summary = await forecast.GetSummaryAsync(project.Id, v2.Id);

        summary!.PreviousVersionId.Should().Be(v1.Id);
        summary.PreviousEstimatedFinal.Should().Be(1_050_000m);
        summary.EstimatedFinalTotal.Should().Be(1_150_000m);
        summary.ChangeSincePrevious.Should().Be(100_000m);
    }

    [Fact]
    public async Task 变化事项拒绝跨项目控制项与缺来源()
    {
        using var db = CreateContext();
        var (p1, elec, _) = await NewProjectAsync(db);
        var p2 = new Project { Code = "P-2", Name = "别的项目" };
        db.Projects.Add(p2);
        await db.SaveChangesAsync();
        var otherItem = new CostControlItem { ProjectId = p2.Id, Code = "X", Name = "别项控制项" };
        db.CostControlItems.Add(otherItem);
        await db.SaveChangesAsync();
        var forecast = new CostForecastService(db);

        var crossProject = () => forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = p1.Id, Code = "V-1", Title = "跨项目",
            Segment = BusinessSegment.MaterialSupply, CostControlItemId = otherItem.Id,
            CostOfficerEstimate = 10m, IncludedInForecast = 10m, Status = ChangeItemStatus.Assessed,
            AsOfDate = new DateOnly(2026, 10, 10), Source = "变更通知"
        });
        await crossProject.Should().ThrowAsync<InvalidOperationException>().WithMessage("*同一项目*");

        var noSource = () => forecast.RegisterChangeItemAsync(new ChangeItem
        {
            ProjectId = p1.Id, Code = "V-2", Title = "缺来源",
            Segment = BusinessSegment.MaterialSupply, CostOfficerEstimate = 10m,
            IncludedInForecast = 10m, Status = ChangeItemStatus.Assessed,
            AsOfDate = new DateOnly(2026, 10, 10), Source = " "
        });
        await noSource.Should().ThrowAsync<InvalidOperationException>().WithMessage("*信息来源*");

        (await db.ChangeItems.CountAsync()).Should().Be(0);
    }
}
