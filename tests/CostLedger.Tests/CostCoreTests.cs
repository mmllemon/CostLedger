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
/// 成本管理内核测试：部位(WBS) 与 工程量清单(BOQ) —— 目标来自清单、按部位归集定位超支。
/// </summary>
public class CostCoreTests
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
    public async Task 目标成本_来自工程量清单()
    {
        using var db = CreateSeededContext();
        (await new BoqService(db).TotalAsync(1)).Should().Be(4_633_000m);

        var spine = await new CostSpineService(db).GetSpineAsync(1);
        spine.Sum(l => l.Target).Should().Be(4_633_000m);
    }

    [Fact]
    public async Task 按部位归集_定位电缆敷设超支()
    {
        using var db = CreateSeededContext();
        var byWbs = await new CostSpineService(db).GetSpineByWbsAsync(1);

        var lay = byWbs.Single(x => x.WbsCode == "W12"); // 电缆敷设
        lay.Target.Should().Be(1_863_000m);
        lay.Incurred.Should().Be(2_000_000m);
        lay.Variance.Should().Be(137_000m);
        lay.IsOver.Should().BeTrue();

        var test = byWbs.Single(x => x.WbsCode == "W3"); // 试验调试
        test.Target.Should().Be(500_000m);
        test.Incurred.Should().Be(400_000m);
        test.IsOver.Should().BeFalse();
    }

    [Fact]
    public async Task 无清单时_目标回退到合同明细()
    {
        using var db = CreateSeededContext();

        // 新项目：只有合同行，没有清单
        var p2 = new Project { Code = "P2", Name = "无清单项目" };
        db.Projects.Add(p2);
        db.SaveChanges();
        var cat = db.CostCategories.Single(c => c.Code == "M-CABLE");
        var party = db.Parties.First();
        var contract = new Contract { Code = "C-P2", ProjectId = p2.Id, PartyId = party.Id, TypeCode = "Purchase", Amount = 800_000m, TaxRate = 0.13m };
        contract.Lines.Add(new ContractLine { CostCategoryId = cat.Id, ItemName = "电缆", Quantity = 1, Unit = "项", UnitPrice = 800_000m, Amount = 800_000m, TaxRate = 0.13m });
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();

        var spine = await new CostSpineService(db).GetSpineAsync(p2.Id);
        spine.Should().ContainSingle();
        spine[0].Target.Should().Be(800_000m); // 回退合同明细
    }

    [Fact]
    public async Task 部位树_全路径()
    {
        using var db = CreateSeededContext();
        var lay = (await new WbsService(db).ListAsync(1)).Single(w => w.Code == "W12");
        lay.FullPath.Should().Be("电缆工程/电缆敷设");
    }
}
