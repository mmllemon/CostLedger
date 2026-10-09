namespace CostLedger.Domain;

/// <summary>
/// 成本脊柱聚合行——按成本科目归集的六类金额口径之一（M1 落地：目标/已承诺/已发生/剩余/偏差）。
/// 它是看板与对账引擎的唯一数据来源：所有业务单据经回写引擎追加 CostEntry，
/// 此处只做"按 (Project, CostCategory) 维度聚合"，不持有任何可变状态。
/// </summary>
public class CostSpineLine
{
    public int CostCategoryId { get; set; }
    public string CostCategoryName { get; set; } = string.Empty;
    public CostCategoryType CategoryType { get; set; }

    /// <summary>目标成本（来自合同明细预算）</summary>
    public decimal Target { get; set; }
    /// <summary>已承诺（合同签约额，按科目映射）</summary>
    public decimal Committed { get; set; }
    /// <summary>已发生（CostEntry 实际发生额）</summary>
    public decimal Incurred { get; set; }
    /// <summary>剩余 = 目标 - 已发生</summary>
    public decimal Remaining => Target - Incurred;
    /// <summary>偏差 = 已发生 - 目标，正数表示超支</summary>
    public decimal Variance => Incurred - Target;
    /// <summary>是否超支</summary>
    public bool IsOver => Variance > 0;
    /// <summary>进度百分比（0-100，超支封顶 100），供 ProgressBar 绑定</summary>
    public double Progress => Target == 0 ? 0 : (double)Math.Min(Incurred / Target, 1m) * 100;
}

/// <summary>成本脊柱服务接口（实现置于 Infrastructure，避免领域层依赖 EF）</summary>
public interface ICostSpineService
{
    Task<List<CostSpineLine>> GetSpineAsync(int projectId, CancellationToken ct = default);
}
