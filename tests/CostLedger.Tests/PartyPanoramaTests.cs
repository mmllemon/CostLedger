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

/// <summary>往来单位全景（新口径）：合同来自合同表，已付/已收来自外部金额事实，未付/未收为推算差额。</summary>
public class PartyPanoramaTests
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
    public async Task 单位全景按项目展示角色与推算未付()
    {
        using var db = CreateContext();
        var p1 = new Project { Code = "P-1", Name = "项目一" };
        var p2 = new Project { Code = "P-2", Name = "项目二" };
        var party = new Party { Name = "远东电缆", RoleCode = "Supplier" };
        db.AddRange(p1, p2, party);
        await db.SaveChangesAsync();

        // 两个项目各一份对下合同
        var c1 = new Contract { ProjectId = p1.Id, PartyId = party.Id, Code = "HT-1", TypeCode = "Sub", Amount = 1_000_000m, SignDate = new DateOnly(2026, 3, 1) };
        var c2 = new Contract { ProjectId = p2.Id, PartyId = party.Id, Code = "HT-2", TypeCode = "Sub", Amount = 800_000m, SignDate = new DateOnly(2026, 4, 1) };
        db.AddRange(c1, c2);
        await db.SaveChangesAsync();

        // 关系（角色）与外部金额事实（已付）
        var factsSvc = new EconomicFactsService(db);
        await factsSvc.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = p1.Id, PartyId = party.Id, RoleCode = PartyRoleCodes.MaterialSupplier,
            Segment = BusinessSegment.MaterialSupply, Direction = EconomicDirection.Expenditure
        });
        // 挂单位的资金状态事实需要同板块关系（方向必须为 已付/已收）
        await factsSvc.RegisterRelationshipAsync(new ProjectPartyRelationship
        {
            ProjectId = p1.Id, PartyId = party.Id, RoleCode = PartyRoleCodes.MaterialSupplier,
            Segment = BusinessSegment.FundingStatus, Direction = EconomicDirection.Payment
        });
        await factsSvc.RegisterFactAsync(new ExternalAmountFact
        {
            ProjectId = p1.Id, PartyId = party.Id, Segment = BusinessSegment.FundingStatus,
            Direction = EconomicDirection.Payment, Amount = 600_000m, AsOfDate = new DateOnly(2026, 10, 6),
            Status = ExternalFactStatus.ExternallyProvided, Precision = ManagementPrecision.ContractPackage, Source = "财务资金日报"
        });

        var l = await new PartyPanoramaService(db).BuildAsync(party.Id);

        l.ContractTotal.Should().Be(1_800_000m);
        l.DownstreamAmount.Should().Be(1_800_000m);
        l.Paid.Should().Be(600_000m);
        l.Unpaid.Should().Be(1_200_000m);        // 180万 − 60万，推算差额
        l.ProjectCount.Should().Be(2);
        l.ContractCount.Should().Be(2);
        l.ByProject.Single(r => r.ProjectName == "项目一").Roles.Should().Contain("材料供应");
        l.ByProject.Single(r => r.ProjectName == "项目一").Unpaid.Should().Be(400_000m); // 100万 − 60万
    }
}
