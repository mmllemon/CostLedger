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

public class EconomicFactsTests
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
    public async Task 同一往来单位可在同一项目承担多个业务角色()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "多角色单位", RoleCode = "LegacyGlobalRole" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();

        db.ProjectPartyRelationships.AddRange(
            new ProjectPartyRelationship
            {
                ProjectId = project.Id, PartyId = party.Id,
                RoleCode = PartyRoleCodes.MaterialSupplier,
                Segment = BusinessSegment.MaterialSupply,
                Direction = EconomicDirection.Expenditure
            },
            new ProjectPartyRelationship
            {
                ProjectId = project.Id, PartyId = party.Id,
                RoleCode = PartyRoleCodes.ProfessionalServiceProvider,
                Segment = BusinessSegment.ProfessionalService,
                Direction = EconomicDirection.Expenditure
            });
        await db.SaveChangesAsync();

        (await db.ProjectPartyRelationships.CountAsync(x => x.PartyId == party.Id)).Should().Be(2);
    }

    [Fact]
    public async Task 项目级外部金额无需清单或科目即可分配到过程控制项()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "ELEC", Name = "电气工程" };
        var fact = new ExternalAmountFact
        {
            ProjectId = project.Id,
            Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure,
            Amount = 100_000m,
            AsOfDate = new DateOnly(2026, 10, 10),
            Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project,
            Source = "项目成本人员汇总",
            ProvidedBy = "项目部"
        };
        db.AddRange(item, fact);
        await db.SaveChangesAsync();

        var allocation = await new EconomicFactsService(db).AllocateAsync(
            fact.Id, item.Id, 60_000m, ManagementPrecision.Professional,
            AllocationStatus.Estimated, "按专业暂估分配");

        allocation.Amount.Should().Be(60_000m);
        allocation.Status.Should().Be(AllocationStatus.Estimated);
        (await new EconomicFactsService(db).ListAllocationsAsync(project.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task 分配拒绝跨项目控制项()
    {
        using var db = CreateContext();
        var p1 = new Project { Code = "P-1", Name = "项目一" };
        var p2 = new Project { Code = "P-2", Name = "项目二" };
        db.AddRange(p1, p2);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = p2.Id, Code = "MATERIAL", Name = "材料" };
        var fact = NewFact(p1.Id, 100m);
        db.AddRange(item, fact);
        await db.SaveChangesAsync();

        var act = () => new EconomicFactsService(db).AllocateAsync(
            fact.Id, item.Id, 20m, ManagementPrecision.ContractPackage,
            AllocationStatus.Allocated);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*必须属于同一项目*");
        (await db.ManagementAllocations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task 分配累计不得超过来源金额()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item1 = new CostControlItem { ProjectId = project.Id, Code = "A", Name = "控制项A" };
        var item2 = new CostControlItem { ProjectId = project.Id, Code = "B", Name = "控制项B" };
        var fact = NewFact(project.Id, 100m);
        db.AddRange(item1, item2, fact);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);
        await service.AllocateAsync(fact.Id, item1.Id, 70m, ManagementPrecision.Project, AllocationStatus.Allocated);

        var act = () => service.AllocateAsync(
            fact.Id, item2.Id, 40m, ManagementPrecision.Project, AllocationStatus.Allocated);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不能超过来源金额*");
        (await db.ManagementAllocations.SumAsync(x => x.Amount)).Should().Be(70m);
    }

    [Fact]
    public async Task 外部事实保留来源日期精度和数据状态()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var fact = NewFact(project.Id, 1_000m);
        db.ExternalAmountFacts.Add(fact);
        await db.SaveChangesAsync();

        var saved = await db.ExternalAmountFacts.SingleAsync();
        saved.Source.Should().Be("项目成本人员汇总");
        saved.AsOfDate.Should().Be(new DateOnly(2026, 10, 10));
        saved.Precision.Should().Be(ManagementPrecision.Project);
        saved.Status.Should().Be(ExternalFactStatus.ExternallyProvided);
    }

    private static ExternalAmountFact NewFact(int projectId, decimal amount) => new()
    {
        ProjectId = projectId,
        Segment = BusinessSegment.ProjectOverhead,
        Direction = EconomicDirection.Expenditure,
        Amount = amount,
        AsOfDate = new DateOnly(2026, 10, 10),
        Status = ExternalFactStatus.ExternallyProvided,
        Precision = ManagementPrecision.Project,
        Source = "项目成本人员汇总"
    };

    [Theory]
    [InlineData(PartyRoleCodes.Owner, BusinessSegment.UpstreamRevenue, EconomicDirection.Income, true)]
    [InlineData(PartyRoleCodes.MaterialSupplier, BusinessSegment.MaterialSupply, EconomicDirection.Expenditure, true)]
    [InlineData(PartyRoleCodes.LaborSubcontractor, BusinessSegment.LaborSubcontract, EconomicDirection.Expenditure, true)]
    [InlineData(PartyRoleCodes.Owner, BusinessSegment.MaterialSupply, EconomicDirection.Expenditure, false)]
    [InlineData(PartyRoleCodes.MaterialSupplier, BusinessSegment.UpstreamRevenue, EconomicDirection.Income, false)]
    [InlineData(PartyRoleCodes.MaterialSupplier, BusinessSegment.FundingStatus, EconomicDirection.Payment, true)]
    [InlineData(PartyRoleCodes.MaterialSupplier, BusinessSegment.FundingStatus, EconomicDirection.Expenditure, false)]
    [InlineData(PartyRoleCodes.InternalProvider, BusinessSegment.ProjectOverhead, EconomicDirection.Expenditure, true)]
    public void 板块决定允许的角色与金额方向(string roleCode, BusinessSegment segment, EconomicDirection direction, bool allowed)
    {
        var violation = BusinessSegmentRules.FindViolation(roleCode, segment, direction);
        if (allowed) violation.Should().BeNull();
        else violation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void 纠正文案使用中文口径名而不是枚举名()
    {
        var roleViolation = BusinessSegmentRules.FindViolation(
            PartyRoleCodes.Owner, BusinessSegment.MaterialSupply, EconomicDirection.Expenditure);
        roleViolation.Should().Contain("材料供应").And.NotContain("MaterialSupply");

        var dirViolation = BusinessSegmentRules.FindViolation(
            PartyRoleCodes.MaterialSupplier, BusinessSegment.FundingStatus, EconomicDirection.Expenditure);
        dirViolation.Should().Contain("项目资金状态").And.NotContain("FundingStatus");
        dirViolation.Should().Contain("已收").And.Contain("已付").And.NotContain("Receipt");
    }

    [Fact]
    public async Task 登记关系拒绝板块与角色不匹配()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "电缆供应商", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();

        var act = () => new EconomicFactsService(db).RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = PartyRoleCodes.MaterialSupplier,
            Segment = BusinessSegment.UpstreamRevenue,
            Direction = EconomicDirection.Income
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*不允许角色*");
        (await db.ProjectPartyRelationships.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task 登记关系拒绝合同跨项目()
    {
        using var db = CreateContext();
        var p1 = new Project { Code = "P-1", Name = "项目一" };
        var p2 = new Project { Code = "P-2", Name = "项目二" };
        var party = new Party { Name = "劳务队", RoleCode = "Labor" };
        db.AddRange(p1, p2, party);
        await db.SaveChangesAsync();
        var contract = new Contract { Code = "C-2", ProjectId = p2.Id, PartyId = party.Id, TypeCode = "LaborSub", Amount = 500m };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();

        var act = () => new EconomicFactsService(db).RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = p1.Id, PartyId = party.Id, ContractId = contract.Id,
            RoleCode = PartyRoleCodes.LaborSubcontractor,
            Segment = BusinessSegment.LaborSubcontract,
            Direction = EconomicDirection.Expenditure
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*同一项目*");
    }

    [Fact]
    public async Task 往来单位金额必须沿用已建立关系的方向()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "建设单位", RoleCode = "Owner" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();
        await new EconomicFactsService(db).RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = PartyRoleCodes.Owner,
            Segment = BusinessSegment.UpstreamRevenue,
            Direction = EconomicDirection.Income
        });

        var fact = await new EconomicFactsService(db).RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, PartyId = party.Id,
            Segment = BusinessSegment.UpstreamRevenue,
            Direction = EconomicDirection.Neutral,
            Amount = 10_000_000m,
            AsOfDate = new DateOnly(2026, 10, 10),
            Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project,
            Source = "施工合同", ProvidedBy = "经营部"
        });

        fact.Direction.Should().Be(EconomicDirection.Income);
    }

    [Fact]
    public async Task 未建立业务关系时拒绝登记往来单位金额()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "未登记单位", RoleCode = "Unknown" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();

        var act = () => new EconomicFactsService(db).RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, PartyId = party.Id,
            Segment = BusinessSegment.ProfessionalService,
            Direction = EconomicDirection.Expenditure,
            Amount = 1000m,
            AsOfDate = new DateOnly(2026, 10, 10),
            Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project,
            Source = "检测合同"
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*尚未在*业务关系*");
    }

    [Fact]
    public async Task 项目级事实无需清单或科目即可汇总经济状态()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);
        var item = new CostControlItem { ProjectId = project.Id, Code = "ELEC", Name = "电气工程" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();

        var income = await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.UpstreamRevenue,
            Direction = EconomicDirection.Income, Amount = 10_000_000m,
            AsOfDate = new DateOnly(2026, 9, 30), Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project, Source = "总包合同"
        });
        var material = await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure, Amount = 4_000_000m,
            AsOfDate = new DateOnly(2026, 10, 5), Status = ExternalFactStatus.PendingConfirmation,
            Precision = ManagementPrecision.ContractPackage, Source = "电缆采购包汇总"
        });
        await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.FundingStatus,
            Direction = EconomicDirection.Receipt, Amount = 6_000_000m,
            AsOfDate = new DateOnly(2026, 10, 8), Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project, Source = "财务部提供收款汇总"
        });
        await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.FundingStatus,
            Direction = EconomicDirection.Payment, Amount = 2_500_000m,
            AsOfDate = new DateOnly(2026, 10, 8), Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Project, Source = "财务部提供付款汇总"
        });
        await service.AllocateAsync(material.Id, item.Id, 1_500_000m,
            ManagementPrecision.Professional, AllocationStatus.Estimated, "按专业暂估");

        var summary = await service.SummarizeProjectAsync(project.Id);

        summary.ExpectedIncome.Should().Be(10_000_000m);
        summary.KnownExpenditure.Should().Be(4_000_000m);
        summary.Received.Should().Be(6_000_000m);
        summary.Paid.Should().Be(2_500_000m);
        summary.FundingGap.Should().Be(2_500_000m - 6_000_000m);
        summary.PendingConfirmationAmount.Should().Be(4_000_000m);
        summary.UnallocatedAmount.Should().Be(4_000_000m - 1_500_000m);
        summary.AllocatableAmount.Should().Be(4_000_000m);
        summary.AllocatedAmount.Should().Be(1_500_000m);
        summary.EstimatedAllocationAmount.Should().Be(1_500_000m);
        summary.LatestAsOfDate.Should().Be(new DateOnly(2026, 10, 8));
        summary.CoverageRate.Should().Be((decimal)1_500_000m / 4_000_000m);
        summary.Segments.Select(x => x.Segment).Should().Contain(new[]
        {
            BusinessSegment.UpstreamRevenue, BusinessSegment.MaterialSupply, BusinessSegment.FundingStatus
        });
        income.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task 控制项汇总保留最粗来源精度且不夸大为清单级()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-1", Name = "项目一" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item = new CostControlItem { ProjectId = project.Id, Code = "ELEC", Name = "电气工程" };
        db.CostControlItems.Add(item);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);

        var coarse = await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.LaborSubcontract,
            Direction = EconomicDirection.Expenditure, Amount = 800_000m,
            AsOfDate = new DateOnly(2026, 10, 1), Status = ExternalFactStatus.ExternallyProvided,
            Precision = ManagementPrecision.Professional, Source = "劳务分包月汇总"
        });
        var fine = await service.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = project.Id, Segment = BusinessSegment.SettlementResult,
            Direction = EconomicDirection.Expenditure, Amount = 200_000m,
            AsOfDate = new DateOnly(2026, 10, 2), Status = ExternalFactStatus.Checked,
            Precision = ManagementPrecision.ContractLine, Source = "结算审定明细"
        });
        await service.AllocateAsync(coarse.Id, item.Id, 800_000m, ManagementPrecision.Professional, AllocationStatus.Allocated);
        await service.AllocateAsync(fine.Id, item.Id, 200_000m, ManagementPrecision.ContractLine, AllocationStatus.Allocated);

        var row = (await service.SummarizeControlItemsAsync(project.Id)).Single();

        row.AllocatedAmount.Should().Be(1_000_000m);
        row.CoarsestSourcePrecision.Should().Be(ManagementPrecision.Professional);
        row.SourceCount.Should().Be(2);
        row.CheckedAmount.Should().Be(200_000m);
    }

    // ---------- 方案 A：角色由字典驱动，板块可增补授权，方向不可放宽 ----------

    private static async Task SeedRoleDictAsync(AppDbContext db, params (string Code, string Name)[] extra)
    {
        var def = new LookupDef { Code = PartyRoleCodes.RoleLookupCode, Name = "项目业务角色" };
        int sort = 1;
        foreach (var code in PartyRoleCodes.BuiltIn)
            def.Items.Add(new LookupItem { LookupDef = def, Code = code, Name = code, SortOrder = sort++ });
        foreach (var (code, name) in extra)
            def.Items.Add(new LookupItem { LookupDef = def, Code = code, Name = name, SortOrder = sort++ });
        db.LookupDefs.Add(def);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task 字典新增角色默认不被板块接受_授权后可登记()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db, ("PlantLease", "机械租赁单位"));
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "某机械租赁公司", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);

        Func<Task> beforeGrant = () => service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure
        });
        await beforeGrant.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不允许角色 PlantLease*板块角色授权*");

        await service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "PlantLease", "现场临设机械租赁计入材料供应口径");

        var saved = await service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure
        });
        saved.RoleCode.Should().Be("PlantLease");
        (await service.ExtraRolesAsync(BusinessSegment.MaterialSupply)).Should().Contain("PlantLease");
    }

    [Fact]
    public async Task 授权不会放宽金额方向()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db, ("PlantLease", "机械租赁单位"));
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "机械租赁", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);
        await service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "PlantLease");

        Func<Task> wrongDirection = () => service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Income
        });
        await wrongDirection.Should().ThrowAsync<InvalidOperationException>().WithMessage("*金额方向只能是*");
    }

    [Fact]
    public async Task 拒绝授权字典中不存在的角色()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db);
        var service = new EconomicFactsService(db);

        Func<Task> grant = () => service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "GhostRole");
        await grant.Should().ThrowAsync<InvalidOperationException>().WithMessage("*尚未在字典*定义*");
        (await db.SegmentRolePermissions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task 停用的字典角色不能再登记业务关系()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db, ("PlantLease", "机械租赁单位"));
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "机械租赁", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);

        var item = await db.LookupItems.SingleAsync(x => x.Code == "PlantLease");
        item.IsEnabled = false;
        await db.SaveChangesAsync();

        Func<Task> register = () => service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure
        });
        await register.Should().ThrowAsync<InvalidOperationException>().WithMessage("*不在字典*启用项*");
    }

    [Fact]
    public async Task 撤权只影响后续登记_不抹掉已建立关系()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db, ("PlantLease", "机械租赁单位"));
        var project = new Project { Code = "P-1", Name = "项目一" };
        var party = new Party { Name = "机械租赁", RoleCode = "Supplier" };
        db.AddRange(project, party);
        await db.SaveChangesAsync();
        var service = new EconomicFactsService(db);
        var grant = await service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "PlantLease");
        await service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure
        });

        await service.RevokeSegmentRoleAsync(grant.Id);

        (await db.ProjectPartyRelationships.CountAsync()).Should().Be(1);
        Func<Task> afterRevoke = () => service.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = project.Id, PartyId = party.Id,
            RoleCode = "PlantLease", Segment = BusinessSegment.MaterialSupply,
            Direction = EconomicDirection.Expenditure
        });
        await afterRevoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*不允许角色*");
    }

    [Fact]
    public async Task 同一角色重复授权不产生第二条记录()
    {
        using var db = CreateContext();
        await SeedRoleDictAsync(db, ("PlantLease", "机械租赁单位"));
        var service = new EconomicFactsService(db);
        var first = await service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "PlantLease");
        var second = await service.GrantSegmentRoleAsync(BusinessSegment.MaterialSupply, "PlantLease");
        second.Id.Should().Be(first.Id);
        (await db.SegmentRolePermissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void 已有字典的旧账套仍能补种项目业务角色()
    {
        // SeedMetadata 遇已有 LookupDefs 会提前 return；新字典必须走独立幂等补种，
        // 否则旧账套升级后角色下拉为空、业务关系根本无法登记（此前 SeedParams 踩过同一坑）。
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        db.LookupDefs.Add(new LookupDef { Code = "PartyRole", Name = "往来单位角色", Items = { new LookupItem { Code = "Supplier", Name = "供方" } } });
        db.SaveChanges();

        SeedData.SeedIfEmpty(db);   // 第二次进入：SeedMetadata 提前 return，补种仍生效

        var def = db.LookupDefs.SingleOrDefault(d => d.Code == PartyRoleCodes.RoleLookupCode);
        def.Should().NotBeNull();
        def!.Items.Select(i => i.Code).Should().Contain(PartyRoleCodes.BuiltIn);
        // 补种幂等：再次运行不产生第二份字典
        SeedData.SeedIfEmpty(db);
        db.LookupDefs.Count(d => d.Code == PartyRoleCodes.RoleLookupCode).Should().Be(1);
    }

    [Fact]
    public async Task 成本控制项可调整编号名称与排序()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-EDIT", Name = "控制项编辑项目" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new EconomicFactsService(db);
        var item = await service.CreateControlItemAsync(project.Id, "MAT-01", "旧名称", 1);

        var updated = await service.UpdateControlItemAsync(item.Id, "MAT-02", "钢材与金属材料", 20);

        updated.Id.Should().Be(item.Id);
        updated.Code.Should().Be("MAT-02");
        updated.Name.Should().Be("钢材与金属材料");
        updated.SortOrder.Should().Be(20);
    }

    [Fact]
    public async Task 成本控制项修改时拒绝重复编号()
    {
        using var db = CreateContext();
        var project = new Project { Code = "P-DUP", Name = "重复编号项目" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new EconomicFactsService(db);
        var first = await service.CreateControlItemAsync(project.Id, "MAT-01", "钢材");
        var second = await service.CreateControlItemAsync(project.Id, "MAT-02", "电缆");

        Func<Task> update = () => service.UpdateControlItemAsync(second.Id, first.Code, "重复编号", 0);
        await update.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已存在*");
    }
}
