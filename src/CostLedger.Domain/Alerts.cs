namespace CostLedger.Domain;

/// <summary>计划任务的类型</summary>
public enum ScheduledTaskKind
{
    RunAlerts   // 评估全部启用的预警规则
}

/// <summary>
/// 预警规则：对某 KPI 指标（复用 DashboardService.Catalog 的指标键）在阈值条件下触发。
/// ProjectId 为空 = 对所有项目评估。全部为"数据"，预警中心后台可维护。
/// </summary>
public class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ProjectId { get; set; }                       // null = 全部项目
    public string MetricKey { get; set; } = string.Empty;      // 指标键（见 DashboardService.Catalog）
    public FilterOperator Operator { get; set; } = FilterOperator.GreaterOrEqual;
    public decimal Threshold { get; set; }
    public KpiSeverity Severity { get; set; } = KpiSeverity.Warn;
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// 预警记录（触发留痕；同一规则+项目仅保留一条未确认记录，重复评估更新数值不重复堆积）。
/// </summary>
public class AlertRecord
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; }
    public decimal Value { get; set; }
    public decimal Threshold { get; set; }
    public KpiSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

/// <summary>
/// 计划任务：按固定间隔运行（如每 60 分钟评估一次预警）。
/// 由后台调度器在到期时触发，或用户手动"立即运行"。
/// </summary>
public class ScheduledTask
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ScheduledTaskKind Kind { get; set; } = ScheduledTaskKind.RunAlerts;
    public int IntervalMinutes { get; set; } = 60;
    public DateTime? LastRunAt { get; set; }
    public int LastRunCount { get; set; }        // 最近一次触发的预警数（便于观测）
    public bool Enabled { get; set; } = true;
}
