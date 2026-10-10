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
        var cat = db.CostCategories.Single(c => c.Code == "EQ-HVCABLE");
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
    public async Task 完工预测_已完工程量驱动EAC()
    {
        using var db = CreateSeededContext();
        var boq = db.BoqLines.Single(b => b.Code == "030408001"); // qty 1000 × 1863
        boq.CompletedQuantity = 600m;   // 已完 600m → 已完金额 1,117,800
        await db.SaveChangesAsync();

        var lay = (await new CostSpineService(db).GetSpineByWbsAsync(1)).Single(x => x.WbsCode == "W12");
        lay.CompletedAmount.Should().Be(1_117_800m);
        lay.Progress.Should().BeApproximately(0.6, 0.001);
        // EAC = 已发生 2,000,000 + 剩余(1,863,000 − 1,117,800) = 2,745,200
        lay.EstimatedFinal.Should().Be(2_745_200m);
    }

    [Fact]
    public async Task 完工预测_未录已完量时回退保守估计()
    {
        using var db = CreateSeededContext();
        var test = (await new CostSpineService(db).GetSpineByWbsAsync(1)).Single(x => x.WbsCode == "W3");
        test.CompletedAmount.Should().Be(0m);
        test.EstimatedFinal.Should().Be(500_000m); // max(目标 50万, 已发生 40万)
    }

    [Fact]
    public async Task 大类目标_优先于清单()
    {
        using var db = CreateSeededContext();
        var cable = db.CostCategories.Single(c => c.Code == "EQ-HVCABLE");
        // 项目 1 原本目标来自清单（电缆 1,863,000）；录科目目标后应以其为准
        await new CostTargetService(db).SetAsync(1, cable.Id, 2_000_000m, "按大类重编");

        var spine = await new CostSpineService(db).GetSpineAsync(1);
        spine.Single(l => l.CostCategoryName == "高压电缆").Target.Should().Be(2_000_000m);
    }

    [Fact]
    public async Task 大类目标_无清单无合同时唯一来源()
    {
        using var db = CreateSeededContext();
        var p2 = new Project { Code = "P3", Name = "只按大类编目标" };
        db.Projects.Add(p2);
        db.SaveChanges();
        var lab = db.CostCategories.Single(c => c.Code == "LAB-INST");
        await new CostTargetService(db).SetAsync(p2.Id, lab.Id, 300_000m, null);

        var spine = await new CostSpineService(db).GetSpineAsync(p2.Id);
        spine.Should().ContainSingle();
        spine[0].Target.Should().Be(300_000m);
    }

    [Fact]
    public async Task 回写_合同挂大类_成本落到该大类()
    {
        using var db = CreateSeededContext();
        var cat = db.CostCategories.Single(c => c.Code == "EQ-HVCABLE");
        var contract = db.Contracts.Single(c => c.Code == "PO-FAR-001");
        contract.CostCategoryId = cat.Id;
        await db.SaveChangesAsync();

        // 回写时不指定科目 → 应回退到合同挂的大类
        await new CostWriteBackService(db).AppendAsync(1, null, contract.Id, VoucherType.Contract, "TEST-1", 100m, new DateOnly(2026, 1, 1));

        var e = db.CostEntries.Single(x => x.SourceVoucherRef == "TEST-1");
        e.CostCategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task 部位树_全路径()
    {
        using var db = CreateSeededContext();
        var lay = (await new WbsService(db).ListAsync(1)).Single(w => w.Code == "W12");
        lay.FullPath.Should().Be("电缆工程/电缆敷设");
    }

    [Fact]
    public async Task 清单导入_按科目与部位解析()
    {
        using var db = CreateSeededContext();
        var csv = "项目编码,项目名称,项目特征,计量单位,工程量,综合单价,合价,成本科目,部位\n" +
                  "030408002,新增测试清单项,特征描述,m,10,100,1000,EQ-HVCABLE,W12\n";

        var res = await new LedgerImportService(db).ImportAsync(1, LedgerKind.Boq, csv);
        res.Created.Should().Be(1);

        var line = db.BoqLines.Single(b => b.Code == "030408002");
        line.Amount.Should().Be(1000m);
        line.CostCategoryId.Should().NotBeNull();   // EQ-HVCABLE
        line.WbsItemId.Should().NotBeNull();        // W12
    }
}
