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
        db.SuppressAudit = true;   // 种子/系统初始化不写审计
        try
        {
            SeedMetadata(db);
            SeedProjectPartyRoleDict(db);   // 幂等补种：SeedMetadata 遇已有字典会提前返回，新字典必须单独补
            SeedParams(db);
            SeedReports(db);            SeedDashboard(db);
            SeedAlerts(db);
            if (db.Projects.Any()) return;
            SeedDemoData(db);
        }
        finally
        {
            db.SuppressAudit = false;
        }
    }

    // ---------- M3-A 预警规则与计划任务（幂等） ----------
    private static void SeedAlerts(AppDbContext db)
    {
        if (db.AlertRules.Any()) return;
        db.AlertRules.AddRange(
            new AlertRule { Name = "超支科目预警", MetricKey = "OverCount", Operator = FilterOperator.GreaterOrEqual, Threshold = 1, Severity = KpiSeverity.Danger, Enabled = true, DisplayOrder = 1 },
            new AlertRule { Name = "总偏差预警", MetricKey = "Variance", Operator = FilterOperator.GreaterOrEqual, Threshold = 30_000m, Severity = KpiSeverity.Danger, Enabled = true, DisplayOrder = 2 },
            new AlertRule { Name = "未收金额预警", MetricKey = "Receivable", Operator = FilterOperator.GreaterOrEqual, Threshold = 1_000_000m, Severity = KpiSeverity.Warn, Enabled = true, DisplayOrder = 3 },
            new AlertRule { Name = "变更净额占比预警", MetricKey = "ChangeRatio", Operator = FilterOperator.GreaterOrEqual, Threshold = 0.10m, Severity = KpiSeverity.Danger, Enabled = true, DisplayOrder = 4 });
        db.ScheduledTasks.Add(new ScheduledTask { Name = "预警评估", Kind = ScheduledTaskKind.RunAlerts, IntervalMinutes = 60, Enabled = true });
        db.SaveChanges();
    }

    // ---------- M2-D 看板默认部件（幂等） ----------
    private static void SeedDashboard(AppDbContext db)
    {
        if (db.DashboardWidgets.Any()) return;
        db.DashboardWidgets.AddRange(
            new DashboardWidget { WidgetKey = "Target", Title = "目标成本", Visible = true, DisplayOrder = 1 },
            new DashboardWidget { WidgetKey = "Incurred", Title = "已发生", Visible = true, DisplayOrder = 2 },
            new DashboardWidget { WidgetKey = "Variance", Title = "总偏差", Visible = true, DisplayOrder = 3, WarnThreshold = 0m, DangerThreshold = 30_000m });
        db.SaveChanges();
    }

    // ---------- M2-C 报表定义（幂等，独立于元数据，便于既有库补种） ----------
    private static void SeedReports(AppDbContext db)
    {
        if (db.ReportDefs.Any()) return;

        var ledger = new ReportDef
        {
            Code = "CONTRACT-LEDGER",
            Name = "合同台账",
            Title = "合同台账",
            DataSourceCode = "Contract",
            Description = "按数据源配置的合同清单（示例）"
        };
        ledger.Columns.Add(new ReportColumn { FieldKey = "Code", Label = "合同编号", Visible = true, DisplayOrder = 1 });
        ledger.Columns.Add(new ReportColumn { FieldKey = "TypeCode", Label = "合同类型", Visible = true, DisplayOrder = 2 });
        ledger.Columns.Add(new ReportColumn { FieldKey = "Title", Label = "合同名称", Visible = true, DisplayOrder = 3 });
        ledger.Columns.Add(new ReportColumn { FieldKey = "Amount", Label = "合同金额", Visible = true, DisplayOrder = 4, Format = "N2", Aggregate = AggregateType.Sum });
        ledger.Columns.Add(new ReportColumn { FieldKey = "Status", Label = "状态", Visible = true, DisplayOrder = 5 });

        db.ReportDefs.Add(ledger);
        db.SaveChanges();
    }

    // ---------- 阶段2 系统参数种子（幂等；值留空由用户在后台「系统参数」填） ----------
    private static void SeedParams(AppDbContext db)
    {
        if (db.SystemParams.Any()) return;
        db.SystemParams.AddRange(
            new SystemParam { Key = "公司名称", Value = "", Description = "打印页眉显示的公司全称（选填）" },
            new SystemParam { Key = "公司地址", Value = "", Description = "打印页眉显示的地址（选填）" });
        db.SaveChanges();
    }

    /// <summary>
    /// 幂等补种「项目业务角色」字典：SeedMetadata 在已有字典的账套会提前 return，
    /// 故新增字典必须单独补种，否则旧账套升级后角色下拉为空、业务关系无法登记。
    /// </summary>
    private static void SeedProjectPartyRoleDict(AppDbContext db)
    {
        if (db.LookupDefs.Any(d => d.Code == "ProjectPartyRole")) return;

        var def = new LookupDef { Code = "ProjectPartyRole", Name = "项目业务角色", Description = "成本岗在业务板块中对该单位的角色认定（可后台增补）", Items = {
            new LookupItem { Code = "Owner", Name = "建设单位/甲方", SortOrder = 1 },
            new LookupItem { Code = "MaterialSupplier", Name = "材料供应单位", SortOrder = 2 },
            new LookupItem { Code = "LaborSubcontractor", Name = "劳务分包单位", SortOrder = 3 },
            new LookupItem { Code = "SpecialistSubcontractor", Name = "专业分包单位", SortOrder = 4 },
            new LookupItem { Code = "ProfessionalServiceProvider", Name = "专业服务单位", SortOrder = 5 },
            new LookupItem { Code = "InternalProvider", Name = "项目部/内部", SortOrder = 6 },
        } };
        db.LookupDefs.Add(def);
        db.SaveChanges();
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
        // 项目业务角色字典由 SeedProjectPartyRoleDict 单独幂等补种（单一来源，见上）
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

        // 其余业务板块（核心字段多为带行项/外键的复杂实体，此处注册以便"字段管理"覆盖全板块；
        // 其详细 CRUD 由对应业务模块承接，扩展字段同样可后台配置）。
        var costCategory = new EntityDef { Code = "CostCategory", Name = "成本科目", Description = "成本归集维度（树形）" };
        costCategory.Fields.Add(new FieldDef { EntityDef = costCategory, EntityDefCode = "CostCategory", Name = "科目编码", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        costCategory.Fields.Add(new FieldDef { EntityDef = costCategory, EntityDefCode = "CostCategory", Name = "科目名称", Type = FieldType.Text, IsRequired = true, DisplayOrder = 2, IsCore = true });
        costCategory.Fields.Add(new FieldDef { EntityDef = costCategory, EntityDefCode = "CostCategory", Name = "默认税率", Type = FieldType.Number, DisplayOrder = 10, IsCore = false });

        var measurement = new EntityDef { Code = "Measurement", Name = "计量与签证", Description = "进度计量 / 现场签证" };
        measurement.Fields.Add(new FieldDef { EntityDef = measurement, EntityDefCode = "Measurement", Name = "计量编号", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        measurement.Fields.Add(new FieldDef { EntityDef = measurement, EntityDefCode = "Measurement", Name = "备注", Type = FieldType.Text, DisplayOrder = 10, IsCore = false });

        var payment = new EntityDef { Code = "Payment", Name = "付款", Description = "预付款/进度款/结算款/质保返还" };
        payment.Fields.Add(new FieldDef { EntityDef = payment, EntityDefCode = "Payment", Name = "付款编号", Type = FieldType.Text, IsRequired = true, DisplayOrder = 1, IsCore = true });
        payment.Fields.Add(new FieldDef { EntityDef = payment, EntityDefCode = "Payment", Name = "金额", Type = FieldType.Money, DisplayOrder = 2, IsCore = true });
        payment.Fields.Add(new FieldDef { EntityDef = payment, EntityDefCode = "Payment", Name = "备注", Type = FieldType.Text, DisplayOrder = 10, IsCore = false });

        db.EntityDefs.AddRange(party, contract, project, costCategory, measurement, payment);
        db.SaveChanges();
    }

    // ---------- M1 演示业务数据（残疾人项目 10KV 外线电缆工程） ----------
    private static void SeedDemoData(AppDbContext db)
    {
        // 成本科目：默认分层级模板（开箱即用、可删改扩，非强制业务分类）
        // 4 根：设备材料 / 人工 / 分包工程 / 其他费用；设备材料下挂配电示例叶，人工下挂劳务示例叶
        var rootEquip = new CostCategory { Code = "EQ", Name = "设备材料", Type = CostCategoryType.EquipmentMaterial, FullPath = "设备材料" };
        var rootLabor = new CostCategory { Code = "LAB", Name = "人工", Type = CostCategoryType.Labor, FullPath = "人工" };
        var rootSub = new CostCategory { Code = "SUB", Name = "分包工程", Type = CostCategoryType.Subcontract, FullPath = "分包工程" };
        var rootOther = new CostCategory { Code = "OTHER", Name = "其他费用", Type = CostCategoryType.Other, FullPath = "其他费用" };
        db.CostCategories.AddRange(rootEquip, rootLabor, rootSub, rootOther);

        var eqLeaves = new[]
        {
            new CostCategory { Code = "EQ-TR", Name = "变压器", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/变压器" },
            new CostCategory { Code = "EQ-HVC", Name = "高压柜", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/高压柜" },
            new CostCategory { Code = "EQ-LVC", Name = "低压柜", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/低压柜" },
            new CostCategory { Code = "EQ-HVCABLE", Name = "高压电缆", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/高压电缆" },
            new CostCategory { Code = "EQ-LVCABLE", Name = "低压电缆", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/低压电缆" },
            new CostCategory { Code = "EQ-BOX", Name = "配电箱", Type = CostCategoryType.EquipmentMaterial, Parent = rootEquip, FullPath = "设备材料/配电箱" },
        };
        var labLeaves = new[]
        {
            new CostCategory { Code = "LAB-INST", Name = "安装劳务", Type = CostCategoryType.Labor, Parent = rootLabor, FullPath = "人工/安装劳务" },
            new CostCategory { Code = "LAB-CIVIL", Name = "土建劳务", Type = CostCategoryType.Labor, Parent = rootLabor, FullPath = "人工/土建劳务" },
        };
        db.CostCategories.AddRange(eqLeaves);
        db.CostCategories.AddRange(labLeaves);

        // 演示数据引用（保持变量名，定位到具体节点）
        // 原则：日常按"大类级"归集——设备材料/人工/分包工程 走各自根节点；
        // 叶子（变压器/电缆/安装劳务…）为可选底层，演示数据不强制落到叶。
        var catCable = eqLeaves.First(c => c.Code == "EQ-HVCABLE");   // 高压电缆（设备材料下示例叶）
        var catSub = rootSub;                                          // 分包工程（大类根）
        var catLabor = rootLabor;                                      // 人工（大类根，大类级归集）

        var project = new Project
        {
            Code = "CZR-2026",
            Name = "残疾人项目 10KV 外线电缆工程",
            OwnerName = "建设单位",
            StartDate = new DateOnly(2025, 3, 14),
            PlannedEndDate = new DateOnly(2025, 9, 12)
        };
        db.Projects.Add(project);

        var owner = new Party { Name = "残疾人项目建设单位", RoleCode = "Owner" };
        var farEast = new Party { Name = "远东电缆", RoleCode = "Supplier" };
        var beiYuan = new Party { Name = "重庆北源", RoleCode = "Subcontractor" };
        var jiChu = new Party { Name = "某基础公司", RoleCode = "Subcontractor" };
        db.Parties.AddRange(owner, farEast, beiYuan, jiChu);

        // 上游合同（我方对甲方，TypeCode=Main）——用于"对上 vs 对下"对账
        var cUp = new Contract
        {
            Code = "MAIN-CZR-001", Project = project, Party = owner, TypeCode = "Main",
            Title = "残疾人项目 10KV 外线电缆工程（总承包）", Amount = 12_480_000m, TaxRate = 0.09m,
            SignDate = new DateOnly(2025, 3, 1), Status = "执行中"
        };
        db.Contracts.Add(cUp);

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

        // 部位（WBS / 分部分项）——成本第二维度，用于定位"哪一段超支"
        var wCable = new WBSItem { Project = project, Code = "W1", Name = "电缆工程", FullPath = "电缆工程", SortOrder = 1 };
        var wTrench = new WBSItem { Project = project, Code = "W11", Name = "电缆排管土建", FullPath = "电缆工程/电缆排管土建", Parent = wCable, SortOrder = 11 };
        var wLay = new WBSItem { Project = project, Code = "W12", Name = "电缆敷设", FullPath = "电缆工程/电缆敷设", Parent = wCable, SortOrder = 12 };
        var wHead = new WBSItem { Project = project, Code = "W13", Name = "电缆终端头", FullPath = "电缆工程/电缆终端头", Parent = wCable, SortOrder = 13 };
        var wBase = new WBSItem { Project = project, Code = "W2", Name = "基础施工", FullPath = "基础施工", SortOrder = 2 };
        var wTest = new WBSItem { Project = project, Code = "W3", Name = "试验调试", FullPath = "试验调试", SortOrder = 3 };
        db.WbsItems.AddRange(wCable, wTrench, wLay, wHead, wBase, wTest);

        // 工程量清单（BOQ）——目标成本来源；合价合计 4,633,000，与原合同口径一致
        db.BoqLines.AddRange(
            new BoqLine { Project = project, Code = "030408001", ItemName = "电力电缆 WDZBN-YJY23-B1 8.7/15kV", Unit = "m", Quantity = 1000, UnitPrice = 1863m, Amount = 1_863_000m, TaxRate = 0.13m, CostCategory = catCable, WbsItem = wLay },
            new BoqLine { Project = project, Code = "030409001", ItemName = "电缆排管土建", Unit = "项", Quantity = 1, UnitPrice = 1_290_000m, Amount = 1_290_000m, TaxRate = 0.09m, CostCategory = catSub, WbsItem = wTrench },
            new BoqLine { Project = project, Code = "030501001", ItemName = "电缆井 / 基础施工", Unit = "项", Quantity = 1, UnitPrice = 980_000m, Amount = 980_000m, TaxRate = 0.09m, CostCategory = catSub, WbsItem = wBase },
            new BoqLine { Project = project, Code = "030411001", ItemName = "试验调试劳务", Unit = "项", Quantity = 1, UnitPrice = 500_000m, Amount = 500_000m, TaxRate = 0.03m, CostCategory = catLabor, WbsItem = wTest });

        // 成本凭证回写（唯一写入入口）。电缆故意超支。
        db.CostEntries.AddRange(
            new CostEntry { Project = project, CostCategory = catCable, Contract = c1, WbsItem = wLay, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "PO-FAR-001", Amount = 2_000_000m, EntryDate = new DateOnly(2025, 6, 1), Note = "电缆采购回写（超支）" },
            new CostEntry { Project = project, CostCategory = catSub, Contract = c2, WbsItem = wTrench, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "SUB-BY-001", Amount = 1_290_000m, EntryDate = new DateOnly(2025, 7, 1) },
            new CostEntry { Project = project, CostCategory = catSub, Contract = c3, WbsItem = wBase, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "SUB-JC-001", Amount = 980_000m, EntryDate = new DateOnly(2025, 7, 1) },
            new CostEntry { Project = project, CostCategory = catLabor, Contract = cLabor, WbsItem = wTest, SourceVoucherType = VoucherType.Contract, SourceVoucherRef = "LAB-001", Amount = 400_000m, EntryDate = new DateOnly(2025, 8, 1) }
        );

        db.SaveChanges();
    }
}
