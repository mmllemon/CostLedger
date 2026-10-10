using CostLedger.Domain;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CostLedger.App.Views;

/// <summary>
/// 经济模块视图共用的展示辅助：中文口径名、金额格式、下拉选项。
/// 提示文案一律用中文口径名，不能把枚举名抛给成本人员。
/// </summary>
internal static class UiText
{
    public static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);
    public static string Date(DateOnly? d) => d is DateOnly x ? x.ToString("yyyy-MM-dd") : "—";

    public static string Segment(BusinessSegment s) => BusinessSegmentRules.DisplayName(s);
    public static string Direction(EconomicDirection d) => BusinessSegmentRules.DisplayName(d);

    public static string Status(ExternalFactStatus s) => s switch
    {
        ExternalFactStatus.Estimated => "估计",
        ExternalFactStatus.ExternallyProvided => "外部提供",
        ExternalFactStatus.Checked => "已核对",
        ExternalFactStatus.Expired => "已过期",
        ExternalFactStatus.PendingConfirmation => "待确认",
        _ => s.ToString()
    };

    public static string Precision(ManagementPrecision p) => p switch
    {
        ManagementPrecision.Project => "项目级",
        ManagementPrecision.Professional => "专业级",
        ManagementPrecision.ContractPackage => "合同包级",
        ManagementPrecision.CostControlItem => "控制项级",
        ManagementPrecision.ContractLine => "清单级",
        _ => p.ToString()
    };

    public static string AllocationStatus(AllocationStatus s) => s switch
    {
        Domain.AllocationStatus.Estimated => "暂估",
        Domain.AllocationStatus.Allocated => "已分配",
        Domain.AllocationStatus.Unallocated => "未分配",
        _ => s.ToString()
    };

    public static string Confidence(ForecastConfidence c) => c switch
    {
        ForecastConfidence.Low => "低",
        ForecastConfidence.Medium => "中",
        ForecastConfidence.High => "高",
        _ => c.ToString()
    };

    public static string VersionStatus(ForecastVersionStatus s)
        => s == ForecastVersionStatus.Frozen ? "已冻结" : "草稿";

    public static string ChangeStatus(ChangeItemStatus s) => s switch
    {
        ChangeItemStatus.Unassessed => "尚未测算",
        ChangeItemStatus.Assessed => "已测算",
        ChangeItemStatus.Submitted => "已报送",
        ChangeItemStatus.ExternalFeedback => "外部反馈",
        ChangeItemStatus.Confirmed => "已确认",
        ChangeItemStatus.NotIncluded => "不纳入预测",
        _ => s.ToString()
    };

    public static IReadOnlyList<Pick<BusinessSegment>> Segments() =>
        System.Enum.GetValues<BusinessSegment>().Select(x => new Pick<BusinessSegment>(x, Segment(x))).ToList();

    public static IReadOnlyList<Pick<EconomicDirection>> Directions() =>
        System.Enum.GetValues<EconomicDirection>().Select(x => new Pick<EconomicDirection>(x, Direction(x))).ToList();

    public static IReadOnlyList<Pick<ExternalFactStatus>> Statuses() =>
        System.Enum.GetValues<ExternalFactStatus>().Select(x => new Pick<ExternalFactStatus>(x, Status(x))).ToList();

    public static IReadOnlyList<Pick<ManagementPrecision>> Precisions() =>
        System.Enum.GetValues<ManagementPrecision>().Select(x => new Pick<ManagementPrecision>(x, Precision(x))).ToList();

    public static IReadOnlyList<Pick<AllocationStatus>> AllocationStatuses() =>
        System.Enum.GetValues<AllocationStatus>().Select(x => new Pick<AllocationStatus>(x, AllocationStatus(x))).ToList();

    public static IReadOnlyList<Pick<ForecastConfidence>> Confidences() =>
        System.Enum.GetValues<ForecastConfidence>().Select(x => new Pick<ForecastConfidence>(x, Confidence(x))).ToList();

    public static IReadOnlyList<Pick<ChangeItemStatus>> ChangeStatuses() =>
        System.Enum.GetValues<ChangeItemStatus>().Select(x => new Pick<ChangeItemStatus>(x, ChangeStatus(x))).ToList();
}

/// <summary>下拉选项：值 + 中文显示名。DisplayMemberBinding 用 Label。</summary>
internal sealed record Pick<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
