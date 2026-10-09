namespace CostLedger.Domain;

/// <summary>成本科目（维度表，支持树形归集，即成本脊柱的纵向键）</summary>
public class CostCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public CostCategory? Parent { get; set; }
    public CostCategoryType Type { get; set; }
    public string? FullPath { get; set; }

    public ICollection<CostCategory> Children { get; set; } = new List<CostCategory>();
    public ICollection<ContractLine> ContractLines { get; set; } = new List<ContractLine>();
    public ICollection<MeasurementLine> MeasurementLines { get; set; } = new List<MeasurementLine>();
    public ICollection<CostEntry> CostEntries { get; set; } = new List<CostEntry>();
}
