namespace CostLedger.Domain;

/// <summary>
/// 预测可信度。由成本人员在建立版本时选定，系统只在数据不足时把上限压低（见 CostForecastService），
/// 不允许输出"看似精确但没有依据"的预计最终成本。
/// </summary>
public enum ForecastConfidence
{
    Low,
    Medium,
    High
}

/// <summary>预测版本的状态。草稿可反复修订；冻结后作为历史对比基线不可再改。</summary>
public enum ForecastVersionStatus
{
    Draft,
    Frozen
}

/// <summary>
/// 成本人员的变化事项测算状态。只描述成本管理信息的进展，不构成 OA 审批结论。
/// </summary>
public enum ChangeItemStatus
{
    Unassessed,
    Assessed,
    Submitted,
    ExternalFeedback,
    Confirmed,
    NotIncluded
}

/// <summary>
/// 成本预测版本：某一时点上"按当前范围与已知信息，项目最终会花多少"的一份可比较底稿。
/// 一个版本 = 预测日期 + 假设 + 一组按成本控制项展开的预测行。修订产生新版本，不覆盖历史。
/// </summary>
public class CostForecastVersion
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    /// <summary>项目内递增的版本号，用于历史比较与报表标注。</summary>
    public int VersionNumber { get; set; }
    public DateOnly ForecastDate { get; set; }
    public ForecastConfidence Confidence { get; set; } = ForecastConfidence.Low;
    /// <summary>编制假设：采用什么价格水平、剩余工程量依据、含不含哪些未确认事项。冻结时必填。</summary>
    public string? Basis { get; set; }
    public ForecastVersionStatus Status { get; set; } = ForecastVersionStatus.Draft;
    public string? Note { get; set; }

    public ICollection<CostForecastLine> Lines { get; set; } = new List<CostForecastLine>();
}

/// <summary>
/// 一条成本控制项的预测。四个数各自独立：目标 / 已知金额 / 剩余估计 / 风险调整。
/// 预计最终是派生值，不是第四个录入项，避免"改结果不改依据"。
/// </summary>
public class CostForecastLine
{
    public int Id { get; set; }
    public int CostForecastVersionId { get; set; }
    public CostForecastVersion? CostForecastVersion { get; set; }
    public int CostControlItemId { get; set; }
    public CostControlItem? CostControlItem { get; set; }

    /// <summary>目标成本（成本人员采用的基准）。</summary>
    public decimal TargetAmount { get; set; }
    /// <summary>已知金额：从外部金额事实按分配归集而来，不是财务实际成本。</summary>
    public decimal KnownAmount { get; set; }
    /// <summary>剩余估计：成本人员对尚未完工部分的估计，允许为 0。</summary>
    public decimal RemainingEstimate { get; set; }
    /// <summary>风险调整：可正可负（涨价/工程量偏差为正，可争取的节约为负）。</summary>
    public decimal RiskAdjustment { get; set; }
    /// <summary>
    /// 该行结论的可信颗粒度上限：等于该控制项所有来源中最粗的精度。
    /// 粗颗粒过程数据存在时，禁止把该行标成清单级精确结果。
    /// </summary>
    public ManagementPrecision PrecisionCap { get; set; } = ManagementPrecision.Project;

    /// <summary>预计最终成本 = 已知 + 剩余估计 + 风险调整。</summary>
    public decimal EstimatedFinal => KnownAmount + RemainingEstimate + RiskAdjustment;
    /// <summary>相对目标的差额，正数表示预计超支。</summary>
    public decimal Variance => TargetAmount == 0 ? 0 : EstimatedFinal - TargetAmount;
    public bool IsOverTarget => TargetAmount > 0 && EstimatedFinal > TargetAmount;
}

/// <summary>
/// 变化事项（设计变更、现场签证、范围调整、价格变化等）的成本影响记录。
/// 系统不制作签证单或变更单，只把"成本人员测算 / 对方报送 / 外部确认 / 纳入当前预测"四个金额分开保存：
/// 成本人员无权把自己的估值写成对方已认可，也不能因对方报送而覆盖自己的测算。
/// </summary>
public class ChangeItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public BusinessSegment Segment { get; set; }
    public int? CostControlItemId { get; set; }
    public CostControlItem? CostControlItem { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    /// <summary>成本人员测算的影响金额（带符号：正=增加成本/收入）。</summary>
    public decimal? CostOfficerEstimate { get; set; }
    /// <summary>对方报送金额（外部提供，未核实）。</summary>
    public decimal? CounterpartySubmitted { get; set; }
    /// <summary>外部确认金额（对方书面认可的数额，非财务入账结论）。</summary>
    public decimal? ExternallyConfirmed { get; set; }
    /// <summary>纳入当前预测的金额，由成本人员决定；与上面三者相互独立。</summary>
    public decimal? IncludedInForecast { get; set; }

    public ChangeItemStatus Status { get; set; } = ChangeItemStatus.Unassessed;
    public DateOnly AsOfDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ProvidedBy { get; set; }
    public string? Note { get; set; }
}

/// <summary>预测版本单行汇总（含变化事项影响与精度封顶），供驾驶舱逐控制项展开。</summary>
public sealed record CostForecastLineSummary(
    int CostControlItemId,
    string Code,
    string Name,
    decimal TargetAmount,
    decimal KnownAmount,
    decimal RemainingEstimate,
    decimal RiskAdjustment,
    decimal EstimatedFinal,
    decimal Variance,
    ManagementPrecision PrecisionCap,
    decimal UnincludedChangeAmount,
    decimal PendingConfirmationChangeAmount);

/// <summary>
/// 项目预测汇总。收入侧与成本侧分栏，另给未归属金额与未纳入预测的变化金额：
/// 这些是"预测还差多少依据"的提示，不是财务结论。
/// </summary>
public sealed record CostForecastSummary(
    int VersionId,
    int VersionNumber,
    DateOnly ForecastDate,
    ForecastConfidence Confidence,
    ForecastVersionStatus Status,
    string? Basis,
    decimal TargetTotal,
    decimal KnownTotal,
    decimal RemainingTotal,
    decimal RiskTotal,
    decimal EstimatedFinalTotal,
    decimal VarianceTotal,
    decimal UnallocatedAmount,
    decimal UnincludedChangeAmount,
    decimal PendingConfirmationChangeAmount,
    int ActiveControlItemCount,
    int ForecastLineCount,
    decimal CoverageRate,
    int? PreviousVersionId,
    decimal PreviousEstimatedFinal,
    decimal ChangeSincePrevious,
    IReadOnlyList<CostForecastLineSummary> Lines);

/// <summary>
/// 预测与变化事项的领域规则。放在领域层，避免 UI 或后台配置绕过成本岗位的口径边界。
/// </summary>
public static class ForecastRules
{
    /// <summary>变化事项四个金额之间的责任边界校验；返回 null 表示成立。</summary>
    public static string? FindViolation(ChangeItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Code))
            return "变化事项必须填写事项编号，便于与现场的变更或签证资料对应。";
        if (item.AsOfDate == default)
            return "变化事项必须填写金额截至日期。";
        if (string.IsNullOrWhiteSpace(item.Source))
            return "变化事项必须填写信息来源（如变更通知、对方报送单、会议纪要）。";

        var included = item.IncludedInForecast;
        switch (item.Status)
        {
            case ChangeItemStatus.Unassessed:
                if (included != null && included != 0)
                    return "尚未测算的变化事项不能填写纳入预测金额。";
                break;

            case ChangeItemStatus.NotIncluded:
                if (included != null && included != 0)
                    return "标记为不纳入当前预测时，纳入金额必须为空或零。";
                break;

            case ChangeItemStatus.Assessed:
                if (item.CostOfficerEstimate == null)
                    return "状态为已测算时必须填写成本人员测算金额。";
                break;

            case ChangeItemStatus.Submitted:
                if (item.CounterpartySubmitted == null)
                    return "状态为已报送对方时必须记录对方报送金额。";
                break;

            case ChangeItemStatus.ExternalFeedback:
                break;

            case ChangeItemStatus.Confirmed:
                if (item.ExternallyConfirmed == null)
                    return "状态为外部确认时必须记录外部确认金额。";
                break;
        }

        if (included == 0 && item.Status != ChangeItemStatus.NotIncluded)
            return "纳入金额为 0 等价于不纳入，请改用不纳入当前预测状态，避免口径歧义。";

        return null;
    }
}
