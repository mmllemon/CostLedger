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

/// <summary>资金方向（蓝图规划：Payment 资金层含收入/支出方向）</summary>
public enum PaymentDirection
{
    Pay,      // 付款（我方对下，支出）
    Receive   // 收款（我方对上，收入）
}

/// <summary>付款类型（2026-10-09 业务口径：预付款/进度款/结算款/质保返还）</summary>
public enum PaymentType
{
    Advance,     // 预付款
    Progress,    // 进度款
    Settlement,  // 结算款
    Retention    // 质保返还
}
