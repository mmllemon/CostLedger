using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// M3-A 预警中心：规则 CRUD + 立即评估 + 预警记录确认 + 计划任务（间隔/立即运行）。
/// 规则驱动、无 AI；后台调度器到期自动运行。
/// </summary>
public partial class AlertCenterView : UserControl
{
    private static readonly string[] OpLabels = { "大于等于", "大于", "等于", "不等于", "小于", "小于等于" };
    private static readonly FilterOperator[] OpValues =
    {
        FilterOperator.GreaterOrEqual, FilterOperator.GreaterThan, FilterOperator.Equals,
        FilterOperator.NotEquals, FilterOperator.LessThan, FilterOperator.LessOrEqual
    };
    private static readonly string[] SevLabels = { "预警", "告警" };

    private readonly AlertService _alerts = null!;
    private readonly SchedulerService _scheduler = null!;
    private int _ruleId;
    private int _taskId;

    public AlertCenterView()
    {
        InitializeComponent();
    }

    public AlertCenterView(AlertService alerts, SchedulerService scheduler) : this()
    {
        _alerts = alerts;
        _scheduler = scheduler;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        RMetric.ItemsSource = DashboardService.Catalog;
        RMetric.DisplayMemberBinding = new Binding("Name");
        RSeverity.ItemsSource = SevLabels;
        ROp.ItemsSource = OpLabels;

        await ReloadRulesAsync();
        await ReloadAlertsAsync();
        await ReloadTasksAsync();
    }

    // ==================== 规则 ====================
    private async Task ReloadRulesAsync(int selectId = 0)
    {
        var list = await _alerts.ListRulesAsync();
        RuleList.ItemsSource = list;
        RuleList.DisplayMemberBinding = new Binding("Name");
        RuleList.SelectedItem = selectId > 0 ? list.FirstOrDefault(r => r.Id == selectId) : list.FirstOrDefault();
    }

    private void OnRuleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RuleList.SelectedItem is not AlertRule r) return;
        _ruleId = r.Id;
        RName.Text = r.Name;
        RMetric.SelectedItem = DashboardService.Catalog.FirstOrDefault(m => m.Key == r.MetricKey);
        ROp.SelectedIndex = Array.IndexOf(OpValues, r.Operator);
        RThreshold.Value = r.Threshold;
        RSeverity.SelectedIndex = r.Severity == KpiSeverity.Danger ? 1 : 0;
        REnabled.IsChecked = r.Enabled;
    }

    private void OnRuleNew(object? sender, RoutedEventArgs e)
    {
        _ruleId = 0;
        RName.Text = "新规则";
        RMetric.SelectedIndex = 0;
        ROp.SelectedIndex = 0;
        RThreshold.Value = 0;
        RSeverity.SelectedIndex = 1;
        REnabled.IsChecked = true;
        RuleList.SelectedItem = null;
    }

    private async void OnRuleSave(object? sender, RoutedEventArgs e)
    {
        if (RMetric.SelectedItem is not KpiMetric metric) return;
        var rule = _ruleId > 0 ? await LoadRule(_ruleId) : new AlertRule();
        rule.Name = RName.Text?.Trim() ?? "";
        rule.MetricKey = metric.Key;
        rule.Operator = OpValues[Math.Clamp(ROp.SelectedIndex, 0, OpValues.Length - 1)];
        rule.Threshold = RThreshold.Value ?? 0m;
        rule.Severity = RSeverity.SelectedIndex == 1 ? KpiSeverity.Danger : KpiSeverity.Warn;
        rule.Enabled = REnabled.IsChecked == true;
        var id = await _alerts.SaveRuleAsync(rule);
        await ReloadRulesAsync(id);
    }

    private async void OnRuleDelete(object? sender, RoutedEventArgs e)
    {
        if (_ruleId == 0) return;
        await _alerts.DeleteRuleAsync(_ruleId);
        _ruleId = 0;
        await ReloadRulesAsync();
        OnRuleNew(null, new RoutedEventArgs());
    }

    private async Task<AlertRule> LoadRule(int id)
        => (await _alerts.ListRulesAsync()).First(r => r.Id == id);

    // ==================== 预警记录 ====================
    private async Task ReloadAlertsAsync()
    {
        AlertList.ItemsSource = await _alerts.ListAlertsAsync(OnlyUnacked.IsChecked == true);
    }

    private async void OnEvaluate(object? sender, RoutedEventArgs e)
    {
        var created = await _alerts.EvaluateAsync();
        await ReloadAlertsAsync();
        AlertStatus.Text = created < 0
            ? "无启用的规则可评估"
            : created == 0 ? "评估完成，无新增预警" : $"评估完成，新增 {created} 条预警";
    }

    private async void OnFilterChanged(object? sender, RoutedEventArgs e) => await ReloadAlertsAsync();

    private async void OnAck(object? sender, RoutedEventArgs e)
    {
        if (AlertList.SelectedItem is not AlertRecord a) return;
        await _alerts.AcknowledgeAsync(a.Id);
        await ReloadAlertsAsync();
    }

    // ==================== 计划任务 ====================
    private async Task ReloadTasksAsync(int selectId = 0)
    {
        var list = await _scheduler.ListTasksAsync();
        TaskList.ItemsSource = list;
        TaskList.DisplayMemberBinding = new Binding("Name");
        TaskList.SelectedItem = selectId > 0 ? list.FirstOrDefault(t => t.Id == selectId) : list.FirstOrDefault();
    }

    private void OnTaskSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TaskList.SelectedItem is not ScheduledTask t) return;
        _taskId = t.Id;
        TName.Text = t.Name;
        TInterval.Value = t.IntervalMinutes;
        TEnabled.IsChecked = t.Enabled;
        TLast.Text = t.LastRunAt is null
            ? "从未运行"
            : $"{t.LastRunAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}（产出 {t.LastRunCount} 条预警）";
    }

    private void OnTaskNew(object? sender, RoutedEventArgs e)
    {
        _taskId = 0;
        TName.Text = "新任务";
        TInterval.Value = 60;
        TEnabled.IsChecked = true;
        TLast.Text = "从未运行";
        TaskList.SelectedItem = null;
    }

    private async void OnTaskSave(object? sender, RoutedEventArgs e)
    {
        var task = _taskId > 0 ? await LoadTask(_taskId) : new ScheduledTask();
        task.Name = TName.Text?.Trim() ?? "";
        task.Kind = ScheduledTaskKind.RunAlerts;
        task.IntervalMinutes = (int)(TInterval.Value ?? 60);
        task.Enabled = TEnabled.IsChecked == true;
        var id = await _scheduler.SaveTaskAsync(task);
        await ReloadTasksAsync(id);
        TaskStatus.Text = "已保存";
    }

    private async void OnTaskDelete(object? sender, RoutedEventArgs e)
    {
        if (_taskId == 0) return;
        await _scheduler.DeleteTaskAsync(_taskId);
        _taskId = 0;
        await ReloadTasksAsync();
        OnTaskNew(null, new RoutedEventArgs());
    }

    private async void OnTaskRun(object? sender, RoutedEventArgs e)
    {
        if (_taskId == 0) return;
        var n = await _scheduler.RunNowAsync(_taskId, DateTime.UtcNow);
        await ReloadTasksAsync(_taskId);
        await ReloadAlertsAsync();
        TaskStatus.Text = n < 0 ? "无规则可评估" : $"运行完成，产出 {n} 条预警";
    }

    private async Task<ScheduledTask> LoadTask(int id)
        => (await _scheduler.ListTasksAsync()).First(t => t.Id == id);
}
