namespace CostLedger.Domain;

/// <summary>往来单位角色</summary>
public enum PartyRole
{
    Owner,          // 甲方 / 建设单位
    Contractor,     // 施工方（上级或总包）
    Supplier,       // 供方 / 材料设备
    Subcontractor,  // 分包
    Supervisor,     // 监理
    Other
}

/// <summary>合同类型</summary>
public enum ContractType
{
    Main,           // 总包 / 主合同
    Subcontract,    // 分包
    Purchase,       // 采购
    Service         // 服务 / 咨询
}

/// <summary>计量与签证类型</summary>
public enum MeasurementType
{
    Progress,       // 进度计量
    Variation       // 签证
}

/// <summary>成本凭证来源（用于反查，凭证式记账）</summary>
public enum VoucherType
{
    Contract,
    Measurement,
    Payment,
    Manual
}

/// <summary>成本科目大类</summary>
public enum CostCategoryType
{
    EquipmentMaterial, // 设备材料
    Subcontract,       // 分包工程
    Labor,             // 人工
    Other
}
