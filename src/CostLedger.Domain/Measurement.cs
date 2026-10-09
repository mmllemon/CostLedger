namespace CostLedger.Domain;

/// <summary>计量与签证（签证 type=Variation，签认后回写成本脊柱，不直接改账）</summary>
public class Measurement
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public MeasurementType Type { get; set; }  // 进度计量 / 签证
    public DateOnly Date { get; set; }
    public string? Title { get; set; }
    public decimal Amount { get; set; }   // 确认金额（含税）
    public bool IsSigned { get; set; }    // 甲方是否已签认
    public bool IsPosted { get; set; }    // 是否已回写成本脊柱
    public string? SourceRef { get; set; }

    public ICollection<MeasurementLine> Lines { get; set; } = new List<MeasurementLine>();
    public ICollection<CostEntry> CostEntries { get; set; } = new List<CostEntry>();
}

/// <summary>计量 / 签证明细行（映射到成本科目）</summary>
public class MeasurementLine
{
    public int Id { get; set; }
    public int MeasurementId { get; set; }
    public Measurement? Measurement { get; set; }
    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
