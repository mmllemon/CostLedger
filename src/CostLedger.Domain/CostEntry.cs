namespace CostLedger.Domain;

/// <summary>
/// 成本凭证（追加式、不可修改的金额分录）。
/// 任何成本发生都只追加一条 CostEntry，通过 SourceVoucherType/SourceVoucherRef 反查来源业务单据，
/// 天然支持审计、幂等回写与版本链。这是成本脊柱回写引擎的唯一写入入口。
/// </summary>
public class CostEntry
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }

    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public int? MeasurementId { get; set; }
    public Measurement? Measurement { get; set; }

    /// <summary>来源单据类型</summary>
    public VoucherType SourceVoucherType { get; set; }
    /// <summary>来源单据号（反查键）</summary>
    public string SourceVoucherRef { get; set; } = string.Empty;

    /// <summary>发生额，正数=成本增加</summary>
    public decimal Amount { get; set; }

    public DateOnly EntryDate { get; set; }
    public string? Note { get; set; }

    // 审计字段
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
