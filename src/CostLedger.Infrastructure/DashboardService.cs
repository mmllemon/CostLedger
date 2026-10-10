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
/// 同时向预警引擎（AlertService）暴露 GetMetricAsync，避免指标算法重复实现（DRY）。
/// 只读展示，不写回、不参与成本脊柱算账（铁律）。
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

    /// <summary>可用 KPI 指标目录（看板设计器 / 预警规则据此列出可选项）。</summary>
    public static IReadOnlyList<KpiMetric> Catalog { get; } = new List<KpiMetric>
    {
        new("Target", "目标成本", "N0", "元", false),
        new("Committed", "已承诺", "N0", "元", true),
        new("Incurred", "已发生", "N0", "元", true),
        new("WorkDoneNotSettled", "已完未结", "N0", "元", true),
        new("Remaining", "剩余", "N0", "元", false),
        new("EstimatedFinal", "预计最终", "N0", "元", false),
        new("Variance", "偏差", "N0", "元", true),
        new("OverCount", "超支科目数", "N0", "个", true),
        new("Receivable", "未收（对甲方）", "N0", "元", true),
        new("ReceivedRatio", "收款比例", "P0", "", false),
        new("Payable", "未付（对下游）", "N0", "元", true),
        new("ChangeNet", "变更净额", "N0", "元", true),
        new("ChangeRatio", "变更净额占比", "P0", "", true),
        new("ContractAmount", "合同总额", "N0", "元", true),
        new("PartyCount", "往来单位数", "N0", "个", false),
        new("ProjectCount", "项目数", "N0", "个", false),
        // ---- 经济口径（v10–v13 新模型；看板与预警共用，单一来源）----
        new("EcoExpectedFinal", "预计最终成本（预测版本）", "N0", "元", true),
        new("EcoOverTarget", "预计超目标（预测版本）", "N0", "元", true),
        new("EcoUnallocated", "未归属金额", "N0", "元", true),
        new("EcoUnincludedChange", "未纳入预测的变化", "N0", "元", true),
        new("EcoExpired", "已过期金额", "N0", "元", true),
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

    // ---------- 指标计算（单一来源，供看板与预警共用） ----------
    /// <summary>
    /// 一次算出某项目的全部指标值（含台账派生指标，供看板与预警共用）。
    /// 经济口径指标直接委托 EconomicFactsService / CostForecastService，避免第二套算法。
    /// </summary>
    public async Task<Dictionary<string, decimal>> ComputeMetricsAsync(int projectId)
    {
        var spine = (await _spine.GetSpineAsync(projectId)).ToList();

        var contracts = await _db.Contracts.Where(c => c.ProjectId == projectId).ToListAsync();
        var upstream = contracts.Where(c => c.TypeCode == "Main").Sum(c => c.Amount);
        var downstream = contracts.Where(c => c.TypeCode != "Main").Sum(c => c.Amount);

        var payments = await _db.Payments
            .Where(p => p.Contract != null && p.Contract.ProjectId == projectId)
            .ToListAsync();
        var received = payments.Where(p => p.Direction == PaymentDirection.Receive).Sum(p => p.Amount);
        var paid = payments.Where(p => p.Direction == PaymentDirection.Pay).Sum(p => p.Amount);

        var changeNet = await _db.ChangeOrders
            .Where(c => c.ProjectId == projectId && c.Status == DocStatus.Confirmed)
            .SumAsync(c => c.Amount);

        // 经济口径（新模型）：委托既有服务，单一来源。
        var economy = await new EconomicFactsService(_db).SummarizeProjectAsync(projectId);
        var forecast = await new CostForecastService(_db).GetSummaryAsync(projectId);

        return new Dictionary<string, decimal>
        {
            ["Target"] = spine.Sum(l => l.Target),
            ["Committed"] = spine.Sum(l => l.Committed),
            ["Incurred"] = spine.Sum(l => l.Incurred),
            ["WorkDoneNotSettled"] = spine.Sum(l => l.WorkDoneNotSettled),
            ["Remaining"] = spine.Sum(l => l.Remaining),
            ["EstimatedFinal"] = spine.Sum(l => l.EstimatedFinal),
            ["Variance"] = spine.Sum(l => l.Variance),
            ["OverCount"] = spine.Count(l => l.IsOver),
            ["Receivable"] = upstream - received,
            ["ReceivedRatio"] = upstream == 0 ? 0m : received / upstream,
            ["Payable"] = downstream - paid,
            ["ChangeNet"] = changeNet,
            ["ChangeRatio"] = upstream == 0 ? 0m : changeNet / upstream,
            ["ContractAmount"] = _db.Contracts.Sum(c => c.Amount),
            ["PartyCount"] = _db.Parties.Count(),
            ["ProjectCount"] = _db.Projects.Count(),
            // 无预测版本时相关项一律为 0，绝不拿已知支出冒充预计最终成本。
            ["EcoExpectedFinal"] = forecast?.EstimatedFinalTotal ?? 0m,
            ["EcoOverTarget"] = forecast is null ? 0m : Math.Max(0m, forecast.VarianceTotal),
            ["EcoUnallocated"] = economy.UnallocatedAmount,
            ["EcoUnincludedChange"] = forecast?.UnincludedChangeAmount ?? 0m,
            ["EcoExpired"] = economy.ExpiredAmount,
        };
    }

    /// <summary>取单个指标值（未知键返回 0）。</summary>
    public async Task<decimal> GetMetricAsync(int projectId, string key)
    {
        var values = await ComputeMetricsAsync(projectId);
        return values.TryGetValue(key, out var v) ? v : 0m;
    }

    // ---------- 计算卡片 ----------
    public async Task<List<KpiCard>> GetCardsAsync(int projectId)
    {
        var values = await ComputeMetricsAsync(projectId);
        var widgets = await _db.DashboardWidgets.Where(w => w.Visible).OrderBy(w => w.DisplayOrder).ToListAsync();
        var cards = new List<KpiCard>();
        foreach (var w in widgets)
        {
            var metric = Metric(w.WidgetKey);
            if (metric is null) continue;
            var value = values.TryGetValue(w.WidgetKey, out var v) ? v : 0m;
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
