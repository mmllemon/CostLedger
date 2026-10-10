using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// M3-A 预警引擎：预警规则 CRUD + 评估（复用 DashboardService 指标，DRY）+ 记录去重 + 确认。
/// 纯规则驱动，不依赖 AI；只读业务数据、只写预警记录，不触碰成本脊柱算账（铁律）。
/// </summary>
public class AlertService
{
    private readonly AppDbContext _db;
    private readonly DashboardService _dashboard;

    public AlertService(AppDbContext db, DashboardService dashboard)
    {
        _db = db;
        _dashboard = dashboard;
    }

    // ---------- 规则 CRUD ----------
    public Task<List<AlertRule>> ListRulesAsync()
        => _db.AlertRules.OrderBy(r => r.DisplayOrder).ToListAsync();

    public async Task<int> SaveRuleAsync(AlertRule rule)
    {
        if (rule.Id == 0) _db.AlertRules.Add(rule);
        else _db.AlertRules.Update(rule);
        await _db.SaveChangesAsync();
        return rule.Id;
    }

    public async Task DeleteRuleAsync(int id)
    {
        var rule = await _db.AlertRules.FindAsync(id);
        if (rule is null) return;
        _db.AlertRules.Remove(rule);
        await _db.SaveChangesAsync();
    }

    // ---------- 预警记录 ----------
    public Task<List<AlertRecord>> ListAlertsAsync(bool onlyUnacknowledged = false)
    {
        var q = _db.AlertRecords.AsQueryable();
        if (onlyUnacknowledged) q = q.Where(a => !a.Acknowledged);
        return q.OrderBy(a => a.Acknowledged).ThenByDescending(a => a.CreatedAt).ToListAsync();
    }

    public async Task AcknowledgeAsync(int id)
    {
        var a = await _db.AlertRecords.FindAsync(id);
        if (a is null) return;
        a.Acknowledged = true;
        a.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ---------- 评估 ----------
    /// <summary>评估全部启用规则；返回本次**新增**的预警数。返回 -1 表示无规则可评估。</summary>
    public async Task<int> EvaluateAsync()
    {
        var rules = await _db.AlertRules.Where(r => r.Enabled).OrderBy(r => r.DisplayOrder).ToListAsync();
        if (rules.Count == 0) return -1;

        var projects = await _db.Projects.ToListAsync();
        int created = 0;

        foreach (var rule in rules)
        {
            var metric = DashboardService.Metric(rule.MetricKey);
            if (metric is null) continue;

            var targets = rule.ProjectId.HasValue
                ? projects.Where(p => p.Id == rule.ProjectId.Value)
                : projects;

            foreach (var p in targets)
            {
                var value = await _dashboard.GetMetricAsync(p.Id, rule.MetricKey);
                if (!Triggered(value, rule.Operator, rule.Threshold)) continue;

                var title = $"{metric.Name} {OpText(rule.Operator)} {rule.Threshold:N0}";
                var message = $"项目「{p.Name}」{metric.Name} 当前 {value:N0}，{OpText(rule.Operator)} 阈值 {rule.Threshold:N0}"
                    + (rule.MetricKey.StartsWith("Eco") ? "（经济口径：为成本管理信息，非财务结论）" : "");

                // 经济口径指标由预测/归属关系派生，超目标等属管理提示，严重度不高于 Warn 以外由规则决定。
                var existing = await _db.AlertRecords
                    .FirstOrDefaultAsync(a => a.RuleId == rule.Id && a.ProjectId == p.Id && !a.Acknowledged);
                if (existing is null)
                {
                    _db.AlertRecords.Add(new AlertRecord
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        ProjectId = p.Id,
                        MetricKey = rule.MetricKey,
                        Operator = rule.Operator,
                        Value = value,
                        Threshold = rule.Threshold,
                        Severity = rule.Severity,
                        Title = title,
                        Message = message,
                        CreatedAt = DateTime.UtcNow
                    });
                    created++;
                }
                else
                {
                    // 同规则+项目未确认记录：刷新数值，不重复堆积
                    existing.Value = value;
                    existing.Title = title;
                    existing.Message = message;
                    existing.Severity = rule.Severity;
                }
            }
        }

        await _db.SaveChangesAsync();
        return created;
    }

    private static bool Triggered(decimal value, FilterOperator op, decimal threshold) => op switch
    {
        FilterOperator.Equals => value == threshold,
        FilterOperator.NotEquals => value != threshold,
        FilterOperator.GreaterThan => value > threshold,
        FilterOperator.LessThan => value < threshold,
        FilterOperator.LessOrEqual => value <= threshold,
        _ => value >= threshold   // GreaterOrEqual 及兜底
    };

    private static string OpText(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "等于",
        FilterOperator.NotEquals => "不等于",
        FilterOperator.GreaterThan => "大于",
        FilterOperator.LessThan => "小于",
        FilterOperator.LessOrEqual => "小于等于",
        _ => "大于等于"
    };
}
