using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>KPI 指标定义（代码注册表）：键、显示名、格式、单位、是否越大越差（阈值判级方向）</summary>
public record KpiMetric(string Key, string Name, string Format, string Unit, bool HigherIsWorse);

/// <summary>看板卡片运行结果（已格式化 + 判级）</summary>
public record KpiCard(string Title, string ValueText, KpiSeverity Severity);

/// <summary>
/// M2-D 看板服务：KPI 指标注册表 + 按配置计算卡片 + 阈值判级。
/// 只读展示，不写回、不参与成本脊柱算账（铁律）。新增指标只需在 Catalog 加一行。
/// </summary>
public class DashboardService
{
    private readonly AppDbContext _db;
    private readonly ICostSpineService _spine;

    public DashboardService(AppDbContext db, ICostSpineService spine)
    {
        _db = db;
        _spine = spine;
    }

    /// <summary>可用 KPI 指标目录（看板设计器据此列出可选项）。</summary>
    public static IReadOnlyList<KpiMetric> Catalog { get; } = new List<KpiMetric>
    {
        new("Target", "目标成本", "N0", "元", false),
        new("Committed", "已承诺", "N0", "元", true),
        new("Incurred", "已发生", "N0", "元", true),
        new("Remaining", "剩余", "N0", "元", false),
        new("Variance", "偏差", "N0", "元", true),
        new("OverCount", "超支科目数", "N0", "个", true),
        new("ContractAmount", "合同总额", "N0", "元", true),
        new("PartyCount", "往来单位数", "N0", "个", false),
        new("ProjectCount", "项目数", "N0", "个", false),
    };

    public static KpiMetric? Metric(string key) => Catalog.FirstOrDefault(m => m.Key == key);

    // ---------- 配置 CRUD ----------
    public Task<List<DashboardWidget>> ListWidgetsAsync()
        => _db.DashboardWidgets.OrderBy(w => w.DisplayOrder).ToListAsync();

    /// <summary>按 WidgetKey 增改（不删除目录外的孤儿，交由设计器显式控制显隐）。</summary>
    public async Task SaveWidgetsAsync(IEnumerable<DashboardWidget> widgets)
    {
        var existing = await _db.DashboardWidgets.ToListAsync();
        foreach (var w in widgets)
        {
            var row = existing.FirstOrDefault(x => x.WidgetKey == w.WidgetKey);
            if (row is null)
            {
                _db.DashboardWidgets.Add(w);
            }
            else
            {
                row.Title = w.Title;
                row.Visible = w.Visible;
                row.DisplayOrder = w.DisplayOrder;
                row.WarnThreshold = w.WarnThreshold;
                row.DangerThreshold = w.DangerThreshold;
            }
        }
        await _db.SaveChangesAsync();
    }

    // ---------- 计算 ----------
    public async Task<List<KpiCard>> GetCardsAsync(int projectId)
    {
        var spine = (await _spine.GetSpineAsync(projectId)).ToList();

        // 指标值（spine 派生 + 实体计数）。新增指标在此补一行 switch 即可。
        decimal MetricValue(string key) => key switch
        {
            "Target" => spine.Sum(l => l.Target),
            "Committed" => spine.Sum(l => l.Committed),
            "Incurred" => spine.Sum(l => l.Incurred),
            "Remaining" => spine.Sum(l => l.Remaining),
            "Variance" => spine.Sum(l => l.Variance),
            "OverCount" => spine.Count(l => l.IsOver),
            "ContractAmount" => _db.Contracts.Sum(c => c.Amount),
            "PartyCount" => _db.Parties.Count(),
            "ProjectCount" => _db.Projects.Count(),
            _ => 0m
        };

        var widgets = await _db.DashboardWidgets.Where(w => w.Visible).OrderBy(w => w.DisplayOrder).ToListAsync();
        var cards = new List<KpiCard>();
        foreach (var w in widgets)
        {
            var metric = Metric(w.WidgetKey);
            if (metric is null) continue;
            var value = MetricValue(w.WidgetKey);
            var text = value.ToString(metric.Format, CultureInfo.CurrentCulture);
            if (!string.IsNullOrEmpty(metric.Unit)) text += " " + metric.Unit;
            cards.Add(new KpiCard(
                string.IsNullOrWhiteSpace(w.Title) ? metric.Name : w.Title,
                text,
                Severity(value, metric.HigherIsWorse, w.WarnThreshold, w.DangerThreshold)));
        }
        return cards;
    }

    private static KpiSeverity Severity(decimal value, bool higherIsWorse, decimal? warn, decimal? danger)
    {
        bool Hit(decimal t) => higherIsWorse ? value >= t : value <= t;
        if (danger.HasValue && Hit(danger.Value)) return KpiSeverity.Danger;
        if (warn.HasValue && Hit(warn.Value)) return KpiSeverity.Warn;
        return KpiSeverity.Normal;
    }
}
