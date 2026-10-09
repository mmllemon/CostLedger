using System;

namespace CostLedger.Domain;

/// <summary>
/// 按部位（WBS）归集的成本行：目标（清单）/ 已发生（凭证）/ 剩余 / 偏差 / 预计最终。
/// 用于"哪一段/哪一部分超支"的定位——成本管理系统区别于二维台账的核心视图。
/// </summary>
public class WbsCostLine
{
    public int WbsItemId { get; set; }
    public string WbsCode { get; set; } = string.Empty;
    public string WbsName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal Incurred { get; set; }
    /// <summary>已完金额 = 已完工程量 × 综合单价（清单口径）</summary>
    public decimal CompletedAmount { get; set; }
    public decimal WorkDoneNotSettled { get; set; }
    public decimal Remaining => Target - Incurred;
    public decimal Variance => Incurred - Target;
    public bool IsOver => Variance > 0;
    /// <summary>完工进度（已完金额 / 目标，0~1）</summary>
    public double Progress => Target == 0 ? 0 : (double)Math.Min(CompletedAmount / Target, 1m);
    /// <summary>
    /// 预计最终（EAC）= 已发生 + 剩余工程量 × 综合单价（= 已发生 + max(0, 目标 − 已完金额)）。
    /// 若未录入已完工程量（CompletedAmount=0），回退为 max(目标, 已发生)（保守，不产生重复计列）。
    /// </summary>
    public decimal EstimatedFinal => CompletedAmount > 0
        ? Incurred + Math.Max(0m, Target - CompletedAmount)
        : Math.Max(Target, Incurred);
}

/// <summary>
/// 部位（WBS / 分部分项）：项目成本的第二维度（第一维是成本科目）。
/// 树形结构（如：电缆工程 → 排管土建 / 电缆敷设 / 终端头）。成本按 科目 × 部位 归集，
/// 用于定位"哪一段/哪一部分超支"。这是"成本管理系统"区别于二维台账的地基。
/// </summary>
public class WBSItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public WBSItem? Parent { get; set; }
    /// <summary>全路径（如 电缆工程/电缆敷设），便于展示与分组</summary>
    public string FullPath { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? Remark { get; set; }
}

/// <summary>
/// 工程量清单行（BOQ）：**目标成本**的来源与最细核算对象之一。
/// 逐项 量 × 综合单价 = 合价；映射 成本科目 与 部位(WBS)。
/// 项目无清单行时，目标成本回退到合同明细（兼容既有数据）。
/// </summary>
public class BoqLine
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    /// <summary>项目编码（如 030408001）</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>项目名称</summary>
    public string ItemName { get; set; } = string.Empty;
    /// <summary>项目特征描述</summary>
    public string? Feature { get; set; }
    /// <summary>计量单位</summary>
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>已完工程量（用于完工预测：剩余工程量 = 工程量 − 已完工程量）</summary>
    public decimal CompletedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>合价 = 工程量 × 综合单价（目标金额）</summary>
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; }

    public int? CostCategoryId { get; set; }
    public CostCategory? CostCategory { get; set; }
    public int? WbsItemId { get; set; }
    public WBSItem? WbsItem { get; set; }

    public string? Remark { get; set; }
}
