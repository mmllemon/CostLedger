using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 批次4 测试：手工成本录入（凭证式）+ 材料收料部位贯通。
/// </summary>
public class ManualCostTests
{
    private static AppDbContext NewContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        SeedData.SeedIfEmpty(db);
        return db;
    }

    [Fact]
    public async Task 手工成本_追加后进入已发生且可列示()
    {
        using var db = NewContext();
        var cat = db.CostCategories.First(c => c.Name == "人工");
        var svc = new ManualCostService(db, new CostWriteBackService(db));

        var ok = await svc.RecordAsync(1, cat.Id, null, null, 120_000m, new DateOnly(2026, 10, 10), "现场经费");
        ok.Should().BeTrue();

        var list = await svc.ListAsync(1);
        list.Should().ContainSingle(e => e.Amount == 120_000m && e.SourceVoucherType == VoucherType.Manual);

        var spine = await new CostSpineService(db).GetSpineAsync(1);
        spine.Should().ContainSingle(l => l.CostCategoryName == "人工")
            .Which.Incurred.Should().Be(400_000m + 120_000m); // 种子 40万 + 手工 12万
    }

    [Fact]
    public async Task 手工成本_冲销后净额归零()
    {
        using var db = NewContext();
        var cat = db.CostCategories.First(c => c.Name == "人工");
        var svc = new ManualCostService(db, new CostWriteBackService(db));

        await svc.RecordAsync(1, cat.Id, null, null, 50_000m, new DateOnly(2026, 10, 10), "机械");
        var entry = (await svc.ListAsync(1)).Single();
        var reversed = await svc.ReverseAsync(entry.Id);
        reversed.Should().BeTrue();

        (await svc.ListAsync(1)).Sum(e => e.Amount).Should().Be(0m);
    }

    [Fact]
    public async Task 材料收料_带部位_回写成本携带WBS()
    {
        using var db = NewContext();
        var cat = db.CostCategories.First(c => c.Name == "人工");
        var wbs = new WBSItem { ProjectId = 1, Code = "W1", Name = "电缆敷设", FullPath = "电缆工程/电缆敷设" };
        db.WbsItems.Add(wbs);
        await db.SaveChangesAsync();

        var mr = new MaterialReceipt
        {
            Code = "MR-T1", ProjectId = 1, CostCategoryId = cat.Id, WbsItemId = wbs.Id,
            MaterialName = "测试", Source = MaterialSource.Self, Amount = 80_000m,
            ReceiptDate = new DateOnly(2026, 10, 10)
        };
        db.MaterialReceipts.Add(mr);
        await db.SaveChangesAsync();

        var mrSvc = new MaterialReceiptService(db, new CostWriteBackService(db));
        await mrSvc.ConfirmAsync(mr.Id);

        var wbsCost = await new CostSpineService(db).GetSpineByWbsAsync(1);
        wbsCost.Should().ContainSingle(l => l.WbsItemId == wbs.Id)
            .Which.Incurred.Should().Be(80_000m);
    }
}
