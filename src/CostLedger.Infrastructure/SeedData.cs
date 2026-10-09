using CostLedger.Domain;

namespace CostLedger.Infrastructure;

/// <summary>
/// M1 演示种子：残疾人项目 10KV 外线电缆工程。
/// 含一个故意超支的科目（电缆采购合同 186.3 万，但回写成本 200 万），用于验证成本脊柱 IsOver 判定。
/// </summary>
public static class SeedData
{
    public static void SeedIfEmpty(AppDbContext db)
    {
        if (db.Projects.Any()) return;

        var catCable = new CostCategory { Code = "M-CABLE", Name = "电力电缆", Type = CostCategoryType.EquipmentMaterial };
        var catSub = new CostCategory { Code = "S-SUB", Name = "分包工程", Type = CostCategoryType.Subcontract };
        var catLabor = new CostCategory { Code = "L-LABOR", Name = "人工", Type = CostCategoryType.Labor };
        db.CostCategories.AddRange(catCable, catSub, catLabor);

        var project = new Project
        {
            Code = "CZR-2026",
            Name = "残疾人项目 10KV 外线电缆工程",
            OwnerName = "建设单位",
            StartDate = new DateOnly(2025, 3, 14),
            PlannedEndDate = new DateOnly(2025, 9, 12)
        };
        db.Projects.Add(project);

        var farEast = new Party { Name = "远东电缆", Role = PartyRole.Supplier };
        var beiYuan = new Party { Name = "重庆北源", Role = PartyRole.Subcontractor };
        var jiChu = new Party { Name = "某基础公司", Role = PartyRole.Subcontractor };
        db.Parties.AddRange(farEast, beiYuan, jiChu);

        var c1 = new Contract
        {
            Code = "PO-FAR-001", Project = project, Party = farEast, Type = ContractType.Purchase,
            Title = "电力电缆采购", Amount = 1_863_000m, TaxRate = 0.13m, SignDate = new DateOnly(2025, 3, 20), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catCable, ItemName = "WDZBN-YJY23-B1 电缆", Quantity = 1000, Unit = "m", UnitPrice = 1863, Amount = 1_863_000m, TaxRate = 0.13m } }
        };
        var c2 = new Contract
        {
            Code = "SUB-BY-001", Project = project, Party = beiYuan, Type = ContractType.Subcontract,
            Title = "电缆敷设分包", Amount = 1_290_000m, TaxRate = 0.09m, SignDate = new DateOnly(2025, 4, 1), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catSub, ItemName = "电缆敷设", Quantity = 1, Unit = "项", UnitPrice = 1_290_000m, Amount = 1_290_000m, TaxRate = 0.09m } }
        };
        var c3 = new Contract
        {
            Code = "SUB-JC-001", Project = project, Party = jiChu, Type = ContractType.Subcontract,
            Title = "基础施工分包", Amount = 980_000m, TaxRate = 0.09m, SignDate = new DateOnly(2025, 4, 5), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catSub, ItemName = "电缆井/基础", Quantity = 1, Unit = "项", UnitPrice = 980_000m, Amount = 980_000m, TaxRate = 0.09m } }
        };
        var cLabor = new Contract
        {
            Code = "LAB-001", Project = project, Party = beiYuan, Type = ContractType.Service,
            Title = "劳务", Amount = 500_000m, TaxRate = 0.03m, SignDate = new DateOnly(2025, 4, 10), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catLabor, ItemName = "劳务", Quantity = 1, Unit = "项", UnitPrice = 500_000m, Amount = 500_000m, TaxRate = 0.03m } }
        };
        db.Contracts.AddRange(c1, c2, c3, cLabor);

        // 成本凭证回写（唯一写入入口）。电缆故意超支。
        db.CostEntries.AddRange(
            new CostEntry { Project = project, CostCategory = catCable, Contract = c1, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "PO-FAR-001", Amount = 2_000_000m, EntryDate = new DateOnly(2025, 6, 1), Note = "电缆采购回写（超支）" },
            new CostEntry { Project = project, CostCategory = catSub, Contract = c2, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "SUB-BY-001", Amount = 1_290_000m, EntryDate = new DateOnly(2025, 7, 1) },
            new CostEntry { Project = project, CostCategory = catSub, Contract = c3, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "SUB-JC-001", Amount = 980_000m, EntryDate = new DateOnly(2025, 7, 1) },
            new CostEntry { Project = project, CostCategory = catLabor, Contract = cLabor, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "LAB-001", Amount = 400_000m, EntryDate = new DateOnly(2025, 8, 1) }
        );

        db.SaveChanges();
    }
}
