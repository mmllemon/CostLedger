namespace CostLedger.Domain;

/// <summary>施工单位成本岗使用的业务板块，不代表财务科目或审批流程。</summary>
public enum BusinessSegment
{
    UpstreamRevenue,
    MaterialSupply,
    LaborSubcontract,
    SpecialistSubcontract,
    ProfessionalService,
    ProjectOverhead,
    FundingStatus,
    CostForecastRisk,
    SettlementResult
}

/// <summary>外部金额的经济方向。收付款是外部状态，不能自动推导成本结论。</summary>
public enum EconomicDirection
{
    Income,
    Expenditure,
    Receipt,
    Payment,
    Neutral
}

/// <summary>成本岗掌握的外部信息状态，不表示会计确认。</summary>
public enum ExternalFactStatus
{
    Estimated,
    ExternallyProvided,
    Checked,
    Expired,
    PendingConfirmation
}

/// <summary>记录本身能支持的管理颗粒度。</summary>
public enum ManagementPrecision
{
    Project,
    Professional,
    ContractPackage,
    CostControlItem,
    ContractLine
}

/// <summary>
/// 内置项目业务角色代码。角色本身由字典 <c>ProjectPartyRole</c> 定义（后台可增补，
/// 如机械租赁单位、检测单位、运输单位），这里只固定"口径骨架"依赖的六个内置代码：
/// 板块允许集合 = 内置代码 ∪ 后台增补授权。<b>业务板块不可增补</b>（仍为强类型枚举）。
/// </summary>
public static class PartyRoleCodes
{
    public const string Owner = "Owner";
    public const string MaterialSupplier = "MaterialSupplier";
    public const string LaborSubcontractor = "LaborSubcontractor";
    public const string SpecialistSubcontractor = "SpecialistSubcontractor";
    public const string ProfessionalServiceProvider = "ProfessionalServiceProvider";
    public const string InternalProvider = "InternalProvider";

    /// <summary>字典 LookupDef.Code：项目业务角色（区别于全局单位档案的 PartyRole）。</summary>
    public const string RoleLookupCode = "ProjectPartyRole";

    public static readonly IReadOnlyList<string> BuiltIn = new[]
    {
        Owner, MaterialSupplier, LaborSubcontractor, SpecialistSubcontractor,
        ProfessionalServiceProvider, InternalProvider
    };
}

/// <summary>来源金额分配状态。Unallocated 允许过程数据暂时无法归类。</summary>
public enum AllocationStatus
{
    Estimated,
    Allocated,
    Unallocated
}

/// <summary>
/// 业务板块规则：板块决定对方单位可承担的角色与金额方向。
/// 施工单位成本岗的对上收入、材料供应、劳务/专业分包、专业服务、间接费用、资金状态口径不同，
/// 不能因为共用同一往来单位就跨板块混用方向。规则放在领域层，避免由 UI 或配置绕过。
/// 角色代码来自字典（可增补），<see cref="extraAllowedRoles"/> 承载后台"板块角色授权"的增补项；
/// 金额方向永远是内置矩阵，配置不得放宽方向。
/// </summary>
public static class BusinessSegmentRules
{
    /// <summary>面向成本岗的中文口径名。提示文案必须用中文名，不能把枚举名抛给用户。</summary>
    private static readonly Dictionary<BusinessSegment, string> SegmentNames = new()
    {
        [BusinessSegment.UpstreamRevenue] = "对上收入",
        [BusinessSegment.MaterialSupply] = "材料供应",
        [BusinessSegment.LaborSubcontract] = "劳务分包",
        [BusinessSegment.SpecialistSubcontract] = "专业分包",
        [BusinessSegment.ProfessionalService] = "专业服务",
        [BusinessSegment.ProjectOverhead] = "项目间接费用",
        [BusinessSegment.FundingStatus] = "项目资金状态",
        [BusinessSegment.CostForecastRisk] = "成本预测与风险",
        [BusinessSegment.SettlementResult] = "结算结果"
    };

    private static readonly Dictionary<EconomicDirection, string> DirectionNames = new()
    {
        [EconomicDirection.Income] = "收入",
        [EconomicDirection.Expenditure] = "支出",
        [EconomicDirection.Receipt] = "已收",
        [EconomicDirection.Payment] = "已付",
        [EconomicDirection.Neutral] = "中性"
    };

    public static string DisplayName(BusinessSegment segment)
        => SegmentNames.GetValueOrDefault(segment, segment.ToString())!;

    public static string DisplayName(EconomicDirection direction)
        => DirectionNames.GetValueOrDefault(direction, direction.ToString())!;

    /// <summary>面向用户展示的允许金额方向（中文，斜杠分隔）。</summary>
    public static string AllowedDirectionsText(BusinessSegment segment)
        => string.Join(" / ", AllowedDirections(segment).Select(DisplayName));

    /// <summary>开放板块（可跨所有角色观察）使用内置全角色；后台增补角色另经 extraAllowedRoles 并入。</summary>
    private static readonly string[] All = PartyRoleCodes.BuiltIn.ToArray();

    private static readonly Dictionary<BusinessSegment, string[]> Roles = new()
    {
        [BusinessSegment.UpstreamRevenue] = new[] { PartyRoleCodes.Owner },
        [BusinessSegment.MaterialSupply] = new[] { PartyRoleCodes.MaterialSupplier },
        [BusinessSegment.LaborSubcontract] = new[] { PartyRoleCodes.LaborSubcontractor },
        [BusinessSegment.SpecialistSubcontract] = new[] { PartyRoleCodes.SpecialistSubcontractor },
        [BusinessSegment.ProfessionalService] = new[] { PartyRoleCodes.ProfessionalServiceProvider },
        [BusinessSegment.ProjectOverhead] = new[] { PartyRoleCodes.InternalProvider },
        [BusinessSegment.FundingStatus] = new[]
        {
            PartyRoleCodes.Owner, PartyRoleCodes.MaterialSupplier, PartyRoleCodes.LaborSubcontractor,
            PartyRoleCodes.SpecialistSubcontractor, PartyRoleCodes.ProfessionalServiceProvider
        },
        [BusinessSegment.CostForecastRisk] = All,
        [BusinessSegment.SettlementResult] = All
    };

    private static readonly Dictionary<BusinessSegment, EconomicDirection[]> Directions = new()
    {
        [BusinessSegment.UpstreamRevenue] = new[] { EconomicDirection.Income, EconomicDirection.Neutral },
        [BusinessSegment.MaterialSupply] = new[] { EconomicDirection.Expenditure, EconomicDirection.Neutral },
        [BusinessSegment.LaborSubcontract] = new[] { EconomicDirection.Expenditure, EconomicDirection.Neutral },
        [BusinessSegment.SpecialistSubcontract] = new[] { EconomicDirection.Expenditure, EconomicDirection.Neutral },
        [BusinessSegment.ProfessionalService] = new[] { EconomicDirection.Expenditure, EconomicDirection.Neutral },
        [BusinessSegment.ProjectOverhead] = new[] { EconomicDirection.Expenditure, EconomicDirection.Neutral },
        [BusinessSegment.FundingStatus] = new[] { EconomicDirection.Receipt, EconomicDirection.Payment },
        [BusinessSegment.CostForecastRisk] = new[] { EconomicDirection.Neutral },
        [BusinessSegment.SettlementResult] = new[]
        {
            EconomicDirection.Income, EconomicDirection.Expenditure, EconomicDirection.Neutral
        }
    };

    public static IReadOnlyCollection<EconomicDirection> AllowedDirections(BusinessSegment segment)
        => Directions.TryGetValue(segment, out var list) ? list : Array.Empty<EconomicDirection>();

    /// <summary>板块内置允许的角色代码（不含后台增补授权）。</summary>
    public static IReadOnlyCollection<string> BuiltInAllowedRoles(BusinessSegment segment)
        => Roles.TryGetValue(segment, out var list) ? list : Array.Empty<string>();

    /// <summary>返回 null 表示组合成立；否则返回可直接展示给成本人员的纠正文案。</summary>
    public static string? FindViolation(
        string roleCode, BusinessSegment segment, EconomicDirection direction,
        IEnumerable<string>? extraAllowedRoles = null)
    {
        if (string.IsNullOrWhiteSpace(roleCode))
            return "业务关系必须指定对方单位在该板块中的角色（来自字典「项目业务角色」）。";

        var allowed = new HashSet<string>(BuiltInAllowedRoles(segment), StringComparer.Ordinal);
        if (extraAllowedRoles != null)
            foreach (var code in extraAllowedRoles)
                if (!string.IsNullOrWhiteSpace(code)) allowed.Add(code);

        if (!allowed.Contains(roleCode))
            return $"业务板块「{DisplayName(segment)}」不允许角色 {roleCode}；如需使用，请在后台「板块角色授权」中增补该角色到此板块。";

        if (!AllowedDirections(segment).Contains(direction))
            return $"业务板块「{DisplayName(segment)}」的金额方向只能是 {AllowedDirectionsText(segment)}，当前为 {DisplayName(direction)}。";

        return null;
    }
}

/// <summary>
/// 后台"板块角色授权"：把字典里新增的角色增补到某业务板块。
/// 只能增补角色，不能新增业务板块、不能改变金额方向（那是强类型口径骨架）。
/// </summary>
public class SegmentRolePermission
{
    public int Id { get; set; }
    public BusinessSegment Segment { get; set; }
    /// <summary>角色代码，须存在于字典 ProjectPartyRole。</summary>
    public string RoleCode { get; set; } = string.Empty;
    public string? Note { get; set; }
}

/// <summary>成本控制项级过程汇总：保留来源精度，不把粗颗粒数据伪装成清单级结果。</summary>
public sealed record CostControlItemSummary(
    int CostControlItemId,
    string Code,
    string Name,
    decimal AllocatedAmount,
    decimal EstimatedAmount,
    /// <summary>来源状态为"已核对"的分配金额；已核对只表示成本岗完成约定范围核对，不是财务确认。</summary>
    decimal CheckedAmount,
    /// <summary>本控制项所有来源中最粗的精度：汇总结论的可信颗粒度由此封顶。</summary>
    ManagementPrecision CoarsestSourcePrecision,
    int SourceCount);

/// <summary>板块金额摘要，供项目经济驾驶舱分栏展示。</summary>
public sealed record SegmentAmountSummary(
    BusinessSegment Segment,
    decimal Amount,
    int FactCount,
    DateOnly? LatestAsOfDate);

/// <summary>
/// 项目经济状态摘要。外部金额事实（收入/支出/收付）与成本人员判断（目标/预测）分栏，
/// 并带未分配金额、暂估金额、覆盖率与数据截至日期；不输出财务结论。
/// </summary>
public sealed record ProjectEconomySummary(
    decimal ExpectedIncome,
    decimal KnownExpenditure,
    decimal Received,
    decimal Paid,
    decimal FundingGap,
    /// <summary>需要归属到成本控制项的金额：材料、劳务、专业分包、专业服务、间接费用与结算结果板块。</summary>
    decimal AllocatableAmount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    decimal EstimatedAllocationAmount,
    /// <summary>AllocatedAmount / AllocatableAmount；资金状态与对上收入金额不计入分母。</summary>
    decimal CoverageRate,
    decimal PendingConfirmationAmount,
    decimal ExpiredAmount,
    DateOnly? LatestAsOfDate,
    IReadOnlyList<SegmentAmountSummary> Segments);

/// <summary>业务板块中需要归属到成本控制项的支出/结算类板块。</summary>
public static class AllocatableSegments
{
    public static readonly IReadOnlyList<BusinessSegment> Values = new[]
    {
        BusinessSegment.MaterialSupply,
        BusinessSegment.LaborSubcontract,
        BusinessSegment.SpecialistSubcontract,
        BusinessSegment.ProfessionalService,
        BusinessSegment.ProjectOverhead,
        BusinessSegment.SettlementResult
    };
}
/// <summary>项目内的往来关系；同一 Party 可在不同项目/合同中承担不同角色。</summary>
public class ProjectPartyRelationship
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    /// <summary>角色代码，取自字典 ProjectPartyRole（后台可增补）。</summary>
    public string RoleCode { get; set; } = string.Empty;
    public BusinessSegment Segment { get; set; }
    public EconomicDirection Direction { get; set; }
    public string? Note { get; set; }
}

/// <summary>成本人员为过程管理建立的稳定口径，不等同合同清单或财务科目。</summary>
public class CostControlItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>成本岗获得的项目金额事实，带来源、截至日期和管理精度。</summary>
public class ExternalAmountFact
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? PartyId { get; set; }
    public Party? Party { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public BusinessSegment Segment { get; set; }
    public EconomicDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public DateOnly AsOfDate { get; set; }
    public ExternalFactStatus Status { get; set; }
    public ManagementPrecision Precision { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ProvidedBy { get; set; }
    public string? Description { get; set; }

    public ICollection<ManagementAllocation> Allocations { get; set; } = new List<ManagementAllocation>();
}

/// <summary>把不同来源、不同粒度的金额映射到项目成本控制项。</summary>
public class ManagementAllocation
{
    public int Id { get; set; }
    public int ExternalAmountFactId { get; set; }
    public ExternalAmountFact? ExternalAmountFact { get; set; }
    public int CostControlItemId { get; set; }
    public CostControlItem? CostControlItem { get; set; }
    public decimal Amount { get; set; }
    public ManagementPrecision Precision { get; set; }
    public AllocationStatus Status { get; set; }
    public string? Note { get; set; }
}
