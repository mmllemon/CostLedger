namespace CostLedger.Domain;

/// <summary>
/// 大类级目标成本：按"成本科目（大类）"直接录入的目标金额（不依赖工程量清单）。
/// 用途：配电项目日常按"大类"（变压器/高压柜/低压柜/高压电缆/低压电缆/配电箱/安装劳务/土建劳务…）管成本，
/// 无需逐条维护清单。
/// 目标口径优先级：科目目标(本表) → 工程量清单(BoqLine) → 合同明细(ContractLine)。
/// </summary>
public class CostTarget
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}
