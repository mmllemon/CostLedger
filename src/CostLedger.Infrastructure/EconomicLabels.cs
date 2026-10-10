namespace CostLedger.Infrastructure;

/// <summary>
/// 经济口径的中文标签与解析：板块 / 方向 / 状态 / 精度 / 分配状态 / 可信度 / 变化事项状态。
/// 放在基础设施层作为单一来源，供界面展示与 Excel 导入共用，避免两处文案漂移。
/// 提示文案一律用中文口径名，不把枚举名抛给成本人员。
/// </summary>
public static class EconomicLabels
{
    public static string Segment(Domain.BusinessSegment s) => Domain.BusinessSegmentRules.DisplayName(s);
    public static string Direction(Domain.EconomicDirection d) => Domain.BusinessSegmentRules.DisplayName(d);

    public static string Status(Domain.ExternalFactStatus s) => s switch
    {
        Domain.ExternalFactStatus.Estimated => "估计",
        Domain.ExternalFactStatus.ExternallyProvided => "外部提供",
        Domain.ExternalFactStatus.Checked => "已核对",
        Domain.ExternalFactStatus.Expired => "已过期",
        Domain.ExternalFactStatus.PendingConfirmation => "待确认",
        _ => s.ToString()
    };

    public static string Precision(Domain.ManagementPrecision p) => p switch
    {
        Domain.ManagementPrecision.Project => "项目级",
        Domain.ManagementPrecision.Professional => "专业级",
        Domain.ManagementPrecision.ContractPackage => "合同包级",
        Domain.ManagementPrecision.CostControlItem => "控制项级",
        Domain.ManagementPrecision.ContractLine => "清单级",
        _ => p.ToString()
    };

    public static string AllocationStatus(Domain.AllocationStatus s) => s switch
    {
        Domain.AllocationStatus.Estimated => "暂估",
        Domain.AllocationStatus.Allocated => "已分配",
        Domain.AllocationStatus.Unallocated => "未分配",
        _ => s.ToString()
    };

    public static string Confidence(Domain.ForecastConfidence c) => c switch
    {
        Domain.ForecastConfidence.Low => "低",
        Domain.ForecastConfidence.Medium => "中",
        Domain.ForecastConfidence.High => "高",
        _ => c.ToString()
    };

    public static string ChangeStatus(Domain.ChangeItemStatus s) => s switch
    {
        Domain.ChangeItemStatus.Unassessed => "尚未测算",
        Domain.ChangeItemStatus.Assessed => "已测算",
        Domain.ChangeItemStatus.Submitted => "已报送",
        Domain.ChangeItemStatus.ExternalFeedback => "外部反馈",
        Domain.ChangeItemStatus.Confirmed => "已确认",
        Domain.ChangeItemStatus.NotIncluded => "不纳入预测",
        _ => s.ToString()
    };

    /// <summary>按中文标签解析业务板块；失败返回 null（导入时作为该行被拒绝的原因）。</summary>
    public static Domain.BusinessSegment? ParseSegment(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.BusinessSegment v in System.Enum.GetValues<Domain.BusinessSegment>())
            if (Segment(v) == t) return v;
        return null;
    }

    public static Domain.EconomicDirection? ParseDirection(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.EconomicDirection v in System.Enum.GetValues<Domain.EconomicDirection>())
            if (Direction(v) == t) return v;
        return null;
    }

    public static Domain.ExternalFactStatus? ParseStatus(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.ExternalFactStatus v in System.Enum.GetValues<Domain.ExternalFactStatus>())
            if (Status(v) == t) return v;
        return null;
    }

    public static Domain.ManagementPrecision? ParsePrecision(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.ManagementPrecision v in System.Enum.GetValues<Domain.ManagementPrecision>())
            if (Precision(v) == t) return v;
        return null;
    }

    public static Domain.AllocationStatus? ParseAllocationStatus(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.AllocationStatus v in System.Enum.GetValues<Domain.AllocationStatus>())
            if (AllocationStatus(v) == t) return v;
        return null;
    }

    public static Domain.ForecastConfidence? ParseConfidence(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.ForecastConfidence v in System.Enum.GetValues<Domain.ForecastConfidence>())
            if (Confidence(v) == t) return v;
        return null;
    }

    public static Domain.ChangeItemStatus? ParseChangeStatus(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        foreach (Domain.ChangeItemStatus v in System.Enum.GetValues<Domain.ChangeItemStatus>())
            if (ChangeStatus(v) == t) return v;
        return null;
    }

    /// <summary>角色中文名 → 内置角色代码（自定义角色按代码原样匹配）。</summary>
    public static string? ParseRoleCode(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        return t switch
        {
            "建设单位" or "甲方" or "建设单位/甲方" => Domain.PartyRoleCodes.Owner,
            "材料供应单位" => Domain.PartyRoleCodes.MaterialSupplier,
            "劳务分包单位" => Domain.PartyRoleCodes.LaborSubcontractor,
            "专业分包单位" => Domain.PartyRoleCodes.SpecialistSubcontractor,
            "专业服务单位" => Domain.PartyRoleCodes.ProfessionalServiceProvider,
            "内部提供单位" => Domain.PartyRoleCodes.InternalProvider,
            _ => t // 未知则原样透传，交由领域层判定是否为字典内角色
        };
    }
}
