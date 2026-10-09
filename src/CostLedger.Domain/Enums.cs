namespace CostLedger.Domain;

/// <summary>
/// 往来单位角色 / 合同类型 已改为字典驱动（LookupDef：PartyRole / ContractType），
/// 见 Metadata.cs，不再使用硬编码枚举。
/// </summary>

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
    Manual,
    ChangeOrder,      // 设计变更/签证
    MaterialReceipt   // 材料收料（乙供）
}

/// <summary>成本科目大类</summary>
public enum CostCategoryType
{
    EquipmentMaterial, // 设备材料
    Subcontract,       // 分包工程
    Labor,             // 人工
    Other
}
