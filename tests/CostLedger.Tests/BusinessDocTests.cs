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
/// 业务单据链路测试：变更令调整目标成本（确认幂等/反确认回落）、材料收料的凭证式回写
/// （乙供回写/甲供不回写），以及发票价税分离、结算金额推导（M2-E P0 缺口）。
/// </summary>
public class BusinessDocTests
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

    private static int CableCategoryId(AppDbContext db)
        => db.CostCategories.Single(c => c.Code == "EQ-HVCABLE").Id;

    [Fact]
    public async Task 变更令确认_调整目标成本且幂等()
    {
        using var db = CreateSeededContext();
        var svc = new ChangeOrderService(db);
        var catId = CableCategoryId(db);

        var id = await svc.SaveAsync(new ChangeOrder
        {
            Code = "CO-001", ProjectId = 1, CostCategoryId = catId,
            Type = ChangeOrderType.SiteVariation, Title = "现场签证", Amount = 50_000m,
            OrderDate = new DateOnly(2026, 1, 10)
        });

        (await svc.ConfirmAsync(id)).Should().BeTrue();
        (await svc.ConfirmAsync(id)).Should().BeFalse(); // 幂等：重复确认不再计入

        var cable = (await new CostSpineService(db).GetSpineAsync(1)).Single(l => l.CostCategoryName == "高压电缆");
        cable.Target.Should().Be(1_913_000m);   // 目标：1,863,000 + 50,000
        cable.Incurred.Should().Be(2_000_000m); // 已发生不受影响
    }

    [Fact]
    public async Task 变更令反确认_目标回落()
    {
        using var db = CreateSeededContext();
        var svc = new ChangeOrderService(db);
        var catId = CableCategoryId(db);

        var id = await svc.SaveAsync(new ChangeOrder
        {
            Code = "CO-002", ProjectId = 1, CostCategoryId = catId,
            Type = ChangeOrderType.DesignChange, Title = "设计变更", Amount = 80_000m,
            OrderDate = new DateOnly(2026, 1, 12)
        });
        await svc.ConfirmAsync(id);
        (await svc.ReopenAsync(id)).Should().BeTrue();

        var cable = (await new CostSpineService(db).GetSpineAsync(1)).Single(l => l.CostCategoryName == "高压电缆");
        cable.Target.Should().Be(1_863_000m); // 回落至原目标
    }

    [Fact]
    public async Task 材料收料_甲供不回写_乙供回写()
    {
        using var db = CreateSeededContext();
        var writer = new CostWriteBackService(db);
        var svc = new MaterialReceiptService(db, writer);
        var catId = CableCategoryId(db);

        var owner = await svc.SaveAsync(new MaterialReceipt
        {
            Code = "MR-O", ProjectId = 1, CostCategoryId = catId, MaterialName = "电缆(甲供)",
            Source = MaterialSource.Owner, Quantity = 10, UnitPrice = 100, ReceiptDate = new DateOnly(2026, 2, 1)
        });
        (await svc.ConfirmAsync(owner)).Should().BeFalse(); // 甲供不计我方成本

        var self = await svc.SaveAsync(new MaterialReceipt
        {
            Code = "MR-S", ProjectId = 1, CostCategoryId = catId, MaterialName = "电缆(乙供)",
            Source = MaterialSource.Self, Quantity = 10, UnitPrice = 200, ReceiptDate = new DateOnly(2026, 2, 3)
        });
        (await svc.ConfirmAsync(self)).Should().BeTrue(); // 乙供计入成本

        var cable = (await new CostSpineService(db).GetSpineAsync(1)).Single(l => l.CostCategoryName == "高压电缆");
        cable.Incurred.Should().Be(2_002_000m); // 2,000,000 + 10×200
    }

    [Fact]
    public async Task 发票_价税分离与进项税合计()
    {
        using var db = CreateSeededContext();
        var svc = new InvoiceService(db);

        var inv = new Invoice
        {
            Code = "INV-1", ProjectId = 1, Direction = InvoiceDirection.Input,
            TaxRate = 0.13m, NetAmount = 100_000m, InvoiceDate = new DateOnly(2026, 3, 1)
        };
        await svc.SaveAsync(inv);

        inv.TaxAmount.Should().Be(13_000m);
        inv.TotalAmount.Should().Be(113_000m);
        (await svc.InputTaxTotalAsync(1)).Should().Be(13_000m);
    }

    [Fact]
    public async Task 结算_金额推导与已审定合计()
    {
        using var db = CreateSeededContext();
        var svc = new SettlementService(db);

        var s = new Settlement
        {
            Code = "ST-1", ProjectId = 1, OriginalAmount = 1_200_000m, AdjustAmount = 48_000m,
            SettleDate = new DateOnly(2026, 4, 1), Status = DocStatus.Confirmed
        };
        await svc.SaveAsync(s);

        s.SettledAmount.Should().Be(1_248_000m);
        (await svc.SettledTotalAsync(1)).Should().Be(1_248_000m);
    }
}
