using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// M1 演示种子 + M2-A 元数据种子。
/// 元数据种子幂等（仅当 LookupDefs 为空时写入），可安全追加到既有 M1 库；
/// 演示业务数据仅在 Projects 为空时写入，避免重复。
/// </summary>
public static class SeedData
{
    public static void SeedIfEmpty(AppDbContext db)
    {
        SeedMetadata(db);
        if (db.Projects.Any()) return;
        SeedDemoData(db);
    }

    // ---------- M2-A 元数据（字典 + 实体/字段注册表） ----------
    private static void SeedMetadata(AppDbContext db)
    {
        if (db.LookupDefs.Any()) return;

        var partyRole = new LookupDef { Code = "PartyRole", Name = "往来单位角色", Description = "甲方/供方/分包/监理…", Items = {
            new LookupItem { Code = "Owner", Name = "甲方/建设单位", SortOrder = 1 },
            new LookupItem { Code = "Contractor", Name = "上级/总包", SortOrder = 2 },
            new LookupItem { Code = "Supplier", Name = "供方/材料设备", SortOrder = 3 },
            new LookupItem { Code = "Subcontractor", Name = "分包", SortOrder = 4 },
            new LookupItem { Code = "Supervisor", Name = "监理", SortOrder = 5 },
            new LookupItem { Code = "Other", Name = "其他", SortOrder = 6 },
        } };
        var contractType = new LookupDef { Code = "ContractType", Name = "合同类型", Items = {
            new LookupItem { Code = "Main", Name = "总包/主合同", SortOrder = 1 },
            new LookupItem { Code = "Subcontract", Name = "分包", SortOrder = 2 },
            new LookupItem { Code = "Purchase", Name = "采购", SortOrder = 3 },
            new LookupItem { Code = "Service", Name = "服务/咨询", SortOrder = 4 },
        } };
        var taxRate = new LookupDef { Code = "TaxRate", Name = "税率模板", Items = {
            new LookupItem { Code = "T13", Name = "13%（设备材料）", SortOrder = 1 },
            new LookupItem { Code = "T09", Name = "9%（分包工程）", SortOrder = 2 },
            new LookupItem { Code = "T03", Name = "3%（劳务简易）", SortOrder = 3 },
            new LookupItem { Code = "T06", Name = "6%（服务）", SortOrder = 4 },
        } };
        var unit = new LookupDef { Code = "Unit", Name = "计量单位", Items = {
            new LookupItem { Code = "M", Name = "m（米）", SortOrder = 1 },
            new LookupItem { Code = "KM", Name = "km（千米）", SortOrder = 2 },
            new LookupItem { Code = "T", Name = "台", SortOrder = 3 },
            new LookupItem { Code = "ITEM", Name = "项", SortOrder = 4 },
            new LookupItem { Code = "M2", Name = "㎡", SortOrder = 5 },
        } };
        var projCat = new LookupDef { Code = "ProjectCategory", Name = "项目属性·工程类别", Items = {
            new LookupItem { Code = "NEW", Name = "新建", SortOrder = 1 },
            new LookupItem { Code = "REN", Name = "改造", SortOrder = 2 },
            new LookupItem { Code = "MOV", Name = "迁改", SortOrder = 3 },
        } };
        var volt = new LookupDef { Code = "VoltageLevel", Name = "项目属性·电压等级", Items = {
            new LookupItem { Code = "V10", Name = "10kV", SortOrder = 1 },
            new LookupItem { Code = "V110", Name = "110kV", SortOrder = 2 },
            new LookupItem { Code = "V220", Name = "220kV", SortOrder = 3 },
        } };
        var stage = new LookupDef { Code = "ProjectStage", Name = "项目属性·阶段", Items = {
            new LookupItem { Code = "DES", Name = "设计", SortOrder = 1 },
            new LookupItem { Code = "CON", Name = "施工", SortOrder = 2 },
            new LookupItem { Code = "SET", Name = "结算", SortOrder = 3 },
        } };
        db.LookupDefs.AddRange(partyRole, contractType, taxRate, unit, projCat, volt, stage);

        // 实体注册表 + 字段注册表（核心字段 IsCore=true；扩展字段 IsCore=false，由动态面板渲染）
        var party = new EntityDef { Code = "Party", Name = "往来单位", Description = "供应商/建设单位/分包等主数据" };
        party.Fields.Add(new FieldDef { EntityDef = party, EntityDefCode = "Party", Name = "单位名称", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        party.Fields.Add(new FieldDef { EntityDef = party, EntityDefCode = "Party", Name = "角色", Type = FieldType.Lookup, LookupDefCode = "PartyRole", IsRequired = true, DisplayOrder = 2, IsCore = true });
        party.Fields.Add(new FieldDef { EntityDef = party, EntityDefCode = "Party", Name = "税号", Type = FieldType.Text, DisplayOrder = 3, IsCore = true });
        party.Fields.Add(new FieldDef { EntityDef = party, EntityDefCode = "Party", Name = "联系人", Type = FieldType.Text, DisplayOrder = 4, IsCore = true });
        party.Fields.Add(new FieldDef { EntityDef = party, EntityDefCode = "Party", Name = "开户行", Type = FieldType.Text, DisplayOrder = 10, IsCore = false });

        var contract = new EntityDef { Code = "Contract", Name = "合同", Description = "合同台账" };
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "合同编号", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "合同类型", Type = FieldType.Lookup, LookupDefCode = "ContractType", IsRequired = true, DisplayOrder = 2, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "合同名称", Type = FieldType.Text, DisplayOrder = 3, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "合同金额", Type = FieldType.Money, DisplayOrder = 4, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "税率", Type = FieldType.Lookup, LookupDefCode = "TaxRate", DisplayOrder = 5, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "签约日期", Type = FieldType.Date, DisplayOrder = 6, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "状态", Type = FieldType.Text, DisplayOrder = 7, IsCore = true });
        contract.Fields.Add(new FieldDef { EntityDef = contract, EntityDefCode = "Contract", Name = "归档号", Type = FieldType.Text, DisplayOrder = 10, IsCore = false });

        var project = new EntityDef { Code = "Project", Name = "项目", Description = "工程项目主数据" };
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "项目编号", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "项目名称", Type = FieldType.Text, IsRequired = true, DisplayOrder = 2, IsCore = true });
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "建设单位", Type = FieldType.Text, DisplayOrder = 3, IsCore = true });
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "工程类别", Type = FieldType.Lookup, LookupDefCode = "ProjectCategory", DisplayOrder = 10, IsCore = false });
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "电压等级", Type = FieldType.Lookup, LookupDefCode = "VoltageLevel", DisplayOrder = 11, IsCore = false });
        project.Fields.Add(new FieldDef { EntityDef = project, EntityDefCode = "Project", Name = "阶段", Type = FieldType.Lookup, LookupDefCode = "ProjectStage", DisplayOrder = 12, IsCore = false });

        db.EntityDefs.AddRange(party, contract, project);
        db.SaveChanges();
    }

    // ---------- M1 演示业务数据（残疾人项目 10KV 外线电缆工程） ----------
    private static void SeedDemoData(AppDbContext db)
    {
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

        var farEast = new Party { Name = "远东电缆", RoleCode = "Supplier" };
        var beiYuan = new Party { Name = "重庆北源", RoleCode = "Subcontractor" };
        var jiChu = new Party { Name = "某基础公司", RoleCode = "Subcontractor" };
        db.Parties.AddRange(farEast, beiYuan, jiChu);

        var c1 = new Contract
        {
            Code = "PO-FAR-001", Project = project, Party = farEast, TypeCode = "Purchase",
            Title = "电力电缆采购", Amount = 1_863_000m, TaxRate = 0.13m, SignDate = new DateOnly(2025, 3, 20), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catCable, ItemName = "WDZBN-YJY23-B1 电缆", Quantity = 1000, Unit = "m", UnitPrice = 1863, Amount = 1_863_000m, TaxRate = 0.13m } }
        };
        var c2 = new Contract
        {
            Code = "SUB-BY-001", Project = project, Party = beiYuan, TypeCode = "Subcontract",
            Title = "电缆敷设分包", Amount = 1_290_000m, TaxRate = 0.09m, SignDate = new DateOnly(2025, 4, 1), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catSub, ItemName = "电缆敷设", Quantity = 1, Unit = "项", UnitPrice = 1_290_000m, Amount = 1_290_000m, TaxRate = 0.09m } }
        };
        var c3 = new Contract
        {
            Code = "SUB-JC-001", Project = project, Party = jiChu, TypeCode = "Subcontract",
            Title = "基础施工分包", Amount = 980_000m, TaxRate = 0.09m, SignDate = new DateOnly(2025, 4, 5), Status = "执行中",
            Lines = { new ContractLine { CostCategory = catSub, ItemName = "电缆井/基础", Quantity = 1, Unit = "项", UnitPrice = 980_000m, Amount = 980_000m, TaxRate = 0.09m } }
        };
        var cLabor = new Contract
        {
            Code = "LAB-001", Project = project, Party = beiYuan, TypeCode = "Service",
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
