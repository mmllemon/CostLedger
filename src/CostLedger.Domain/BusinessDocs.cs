namespace CostLedger.Domain;

/// <summary>变更令类型</summary>
public enum ChangeOrderType
{
    DesignChange,   // 设计变更
    SiteVariation,  // 现场签证
    Claim           // 索赔
}

/// <summary>单据状态</summary>
public enum DocStatus
{
    Draft,      // 草稿
    Confirmed   // 已确认 / 已审定
}

/// <summary>材料来源</summary>
public enum MaterialSource
{
    Self,   // 乙供（我方采购）→ 计入我方成本
    Owner   // 甲供（甲方供应）→ 不计我方采购成本，仅登记/对账
}

/// <summary>发票方向</summary>
public enum InvoiceDirection
{
    Input,   // 进项（我方收票，可抵扣）
    Output   // 销项（我方开票）
}

/// <summary>发票种类</summary>
public enum InvoiceCategory
{
    Special,  // 增值税专用发票
    Normal    // 增值税普通发票
}

/// <summary>
/// 设计变更 / 现场签证 / 索赔令（P0 缺口之一）。
/// 确认后经 CostWriteBackService 追加一条 CostEntry（凭证式、幂等），成本进入「已发生」。
/// Amount 为带符号金额：正=增加成本，负=减少成本。
/// </summary>
public class ChangeOrder
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }

    public ChangeOrderType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }          // 带符号：正=增加
    public DateOnly OrderDate { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Draft;
    public string? Note { get; set; }
}

/// <summary>
/// 材料收料单（P0 缺口之一，货流）。
/// 乙供（Self）：确认后追加 CostEntry（材料成本）；
/// 甲供（Owner）：不写我方成本，仅登记用于扣款/对账。
/// </summary>
public class MaterialReceipt
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }

    /// <summary>成本归集的部位（WBS，可空）。确认回写时携带，用于按部位成本定位。</summary>
    public int? WbsItemId { get; set; }
    public WBSItem? WbsItem { get; set; }

    public string MaterialName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }          // 含税金额
    public MaterialSource Source { get; set; } = MaterialSource.Self;
    public DateOnly ReceiptDate { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Draft;
    public string? Note { get; set; }
}

/// <summary>
/// 发票税务台账（P0 缺口之一，发票流）。
/// 价税分离：NetAmount(不含税) + TaxAmount(税额) = TotalAmount(价税合计)。
/// 不写成本脊柱；供进项税台账、与合同/付款勾稽。
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;   // 发票号码
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? PartyId { get; set; }
    public Party? Party { get; set; }

    public InvoiceDirection Direction { get; set; } = InvoiceDirection.Input;
    public InvoiceCategory Category { get; set; } = InvoiceCategory.Special;
    public decimal TaxRate { get; set; }               // 0.13 / 0.09 / 0.03
    public decimal NetAmount { get; set; }             // 不含税金额
    public decimal TaxAmount { get; set; }             // 税额
    public decimal TotalAmount { get; set; }           // 价税合计
    public DateOnly InvoiceDate { get; set; }
    public string? Status { get; set; }                // 已收票/已认证/已抵扣
    public string? Note { get; set; }
}

/// <summary>
/// 竣工结算（P0 缺口之一）。
/// 记录结算金额与原合同额、调整额，用于锁定「预计最终」口径。
/// </summary>
public class Settlement
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public decimal OriginalAmount { get; set; }   // 原合同额
    public decimal AdjustAmount { get; set; }     // 调整额
    public decimal SettledAmount { get; set; }    // 结算金额
    public DateOnly SettleDate { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Draft;
    public string? Note { get; set; }
}
