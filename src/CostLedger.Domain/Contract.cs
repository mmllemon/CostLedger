namespace CostLedger.Domain;

/// <summary>合同（合并原合同管理 + 合同台账，统一模型）</summary>
public class Contract
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int PartyId { get; set; }
    public Party? Party { get; set; }
    /// <summary>合同类型代码，引用 LookupDef "ContractType" 的 LookupItem.Code（字典驱动，取代原硬编码枚举）</summary>
    public string TypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public decimal Amount { get; set; }   // 合同金额（含税）
    public decimal TaxRate { get; set; }  // 税率 0.09 / 0.13 / 0.03
    public DateOnly SignDate { get; set; }
    public string? Status { get; set; }

    // —— 合同级可配 ——
    /// <summary>质保金比例（0~1，逐合同可配；仅作台账信息，系统不做抵扣计算）</summary>
    public decimal RetentionRate { get; set; }

    /// <summary>成本大类（成本科目）：该合同费用归属的成本科目，用于"实际/承诺按大类归集"（可选）</summary>
    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }

    public ICollection<ContractLine> Lines { get; set; } = new List<ContractLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<CostEntry> CostEntries { get; set; } = new List<CostEntry>();
    public ICollection<ProjectPartyRelationship> ProjectPartyRelationships { get; set; } = new List<ProjectPartyRelationship>();
}

/// <summary>合同明细行（金额映射到成本科目，是目标成本的来源）</summary>
public class ContractLine
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }   // 含税金额
    public decimal TaxRate { get; set; }
}
