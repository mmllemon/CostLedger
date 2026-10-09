namespace CostLedger.Domain;

/// <summary>KPI 卡片的风险级别（由阈值判级，驱动颜色）</summary>
public enum KpiSeverity { Normal, Warn, Danger }

/// <summary>
/// M2-D 看板部件：用户对"要显示哪些 KPI、什么顺序、阈值多少"的配置。
/// WidgetKey 对应 Infrastructure.DashboardService 中的指标注册表。
/// 全部为"数据"，看板设计器配置即生效，无需改代码。
/// </summary>
public class DashboardWidget
{
    public int Id { get; set; }
    public string WidgetKey { get; set; } = string.Empty;  // 指标键，如 "Incurred"
    public string Title { get; set; } = string.Empty;      // 卡片标题（可覆盖默认）
    public bool Visible { get; set; } = true;
    public int DisplayOrder { get; set; }
    public decimal? WarnThreshold { get; set; }            // 预警阈值（null=不判级）
    public decimal? DangerThreshold { get; set; }          // 告警阈值
}
