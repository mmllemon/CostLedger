using System;
using System.Collections.Generic;

namespace CostLedger.Domain;

/// <summary>外部成果结果的类型（系统本身不制作这些成果文件，只登记其金额结果）。</summary>
public enum ResultRecordType
{
    ProgressMeasurement,   // 进度款报量
    SiteMeasurement,       // 收方单
    AuditSettlement,       // 送审结算
    ApprovedSettlement     // 审定结算
}

/// <summary>
/// 外部成果结果（P4）：把外部造价软件编制、并已对外报送或已获确认的关键金额结果登记进来，
/// 用于成本分析与结算对目标/预测的差异，**不生成报量表/签证单/收方单/送审结算书**，
/// 也不保存图纸、计价依据、报价来源或计算底稿。
/// 两个金额相互独立、互不覆盖：报送额（我方对外报出）与确认额（外部核对/审定回执）。
/// </summary>
public class ExternalResultRecord
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>期次或批次编号（如 2026-09 进度款、第 3 期）。</summary>
    public string Period { get; set; } = string.Empty;
    public ResultRecordType Type { get; set; }

    /// <summary>关联的收入类业务板块方向（默认结算结果），用于归入收入侧口径。</summary>
    public BusinessSegment Segment { get; set; } = BusinessSegment.SettlementResult;

    /// <summary>我方对外报送金额（成果由外部造价软件编制）。</summary>
    public decimal SubmittedAmount { get; set; }
    /// <summary>外部核对/审定回执金额（对方书面数额，非财务入账结论）。</summary>
    public decimal ConfirmedAmount { get; set; }

    public int? CostControlItemId { get; set; }
    public CostControlItem? CostControlItem { get; set; }

    public DateOnly AsOfDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ProvidedBy { get; set; }
    public string? Note { get; set; }

    public decimal Variance => ConfirmedAmount - SubmittedAmount;
}

/// <summary>外部成果结果领域规则：来源与期次必填、金额非负、截至日期必填。</summary>
public static class ExternalResultRules
{
    public static string? FindViolation(ExternalResultRecord r)
    {
        if (string.IsNullOrWhiteSpace(r.Period))
            return "外部成果结果必须填写期次或批次编号。";
        if (string.IsNullOrWhiteSpace(r.Source))
            return "外部成果结果必须填写来源（成果文件名称或报送渠道）。";
        if (r.AsOfDate == default)
            return "外部成果结果必须填写金额截至日期。";
        if (r.SubmittedAmount < 0 || r.ConfirmedAmount < 0)
            return "报送金额与确认金额不能为负。";
        return null;
    }
}

/// <summary>按项目汇总外部成果结果（收入侧）：报送合计、确认合计、差额，按类型与期次展开。</summary>
public sealed record ExternalResultSummary(
    decimal SubmittedTotal,
    decimal ConfirmedTotal,
    decimal VarianceTotal,
    DateOnly? LatestAsOfDate,
    IReadOnlyList<ExternalResultLine> Lines);

/// <summary>外部成果结果的一行（按期次/类型）。</summary>
public sealed record ExternalResultLine(
    int Id,
    string Period,
    ResultRecordType Type,
    decimal SubmittedAmount,
    decimal ConfirmedAmount,
    decimal Variance,
    DateOnly AsOfDate,
    string Source,
    string? ControlItemName);

/// <summary>
/// 结算结果对目标/预测的差异（P4）。三个口径并列、**不加总**：
/// 报送/确认是外部成果结果（收入侧），预计收入来自对上收入事实，预计最终成本来自预测版本。
/// 差额只作管理提示，不是财务结论；无预测版本时成本侧为 null。
/// </summary>
public sealed record SettlementVarianceSummary(
    decimal SubmittedRevenue,
    decimal ConfirmedRevenue,
    decimal ExpectedIncome,
    decimal RevenueVarianceVsSubmitted,
    decimal RevenueVarianceVsExpected,
    decimal? EstimatedFinalCost,
    decimal? TargetCost,
    bool HasForecast,
    DateOnly? LatestAsOfDate);
