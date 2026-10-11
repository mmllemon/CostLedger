using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 预警中心（新视觉）：规则 CRUD + 立即评估 + 记录确认 + 计划任务。
/// 规则驱动、无 AI；指标来自 DashboardService.Catalog（含经济口径）。
/// 预警是管理提示，不是财务结论；对 Eco* 指标的文案已带「非财务结论」限定。
/// </summary>
public class AlertCenterView : UserControl
{
    private static readonly (string Label, FilterOperator Value)[] Ops =
    {
        ("大于等于", FilterOperator.GreaterOrEqual), ("大于", FilterOperator.GreaterThan),
        ("等于", FilterOperator.Equals), ("不等于", FilterOperator.NotEquals),
        ("小于", FilterOperator.LessThan), ("小于等于", FilterOperator.LessOrEqual)
    };

    private readonly AlertService _alerts = null!;
    private readonly SchedulerService _scheduler = null!;

    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
    private readonly TextBlock _subtitle = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _kpis = Widgets.KpiGrid(3);
    private readonly StackPanel _recordHost = new();
    private readonly StackPanel _ruleHost = new();
    private readonly StackPanel _taskHost = new();
    private bool _onlyUnacked = true;

    public AlertCenterView() { }

    public AlertCenterView(AlertService alerts, SchedulerService scheduler)
    {
        _alerts = alerts;
        _scheduler = scheduler;
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        var top = Widgets.PageHeaderWithDynamicSubtitle("预警", "预警中心",
            "规则驱动；指标含经济口径（预计超目标/未归属/已过期等）", _subtitle,
            Widgets.PrimaryActionButton("立即评估", () => _ = EvaluateAsync()),
            Widgets.ActionButton("新建规则", () => OpenRule(null)),
            Widgets.ActionButton("仅未确认/全部", ToggleFilter));

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card(null, _kpis));
        root.Children.Add(Widgets.Card("预警记录（点击「确认」标记已处理）", _recordHost));
        root.Children.Add(Widgets.Card("预警规则", _ruleHost));
        root.Children.Add(Widgets.Card("计划任务（按间隔自动评估预警规则）", _taskHost));
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task InitAsync()
    {
        await ReloadAllAsync();
    }

    private async Task ReloadAllAsync()
    {
        await ReloadAlertsAsync();
        await ReloadRulesAsync();
        await ReloadTasksAsync();
    }

    private void ToggleFilter()
    {
        _onlyUnacked = !_onlyUnacked;
        _ = ReloadAlertsAsync();
    }

    // ==================== 记录 ====================
    private async Task ReloadAlertsAsync()
    {
        var all = await _alerts.ListAlertsAsync(false);
        var records = _onlyUnacked ? all.Where(a => !a.Acknowledged).ToList() : all;

        _subtitle.Text = _onlyUnacked ? $"仅未确认 · {records.Count} 条" : $"全部 · {records.Count} 条";
        _kpis.Children.Clear();
        _kpis.Children.Add(Widgets.Kpi("未确认预警", all.Count(a => !a.Acknowledged).ToString(), all.Any(a => !a.Acknowledged)));
        _kpis.Children.Add(Widgets.Kpi("告警（高）", all.Count(a => !a.Acknowledged && a.Severity == KpiSeverity.Danger).ToString()));
        _kpis.Children.Add(Widgets.Kpi("规则数", (await _alerts.ListRulesAsync()).Count.ToString()));

        _recordHost.Children.Clear();
        if (records.Count == 0) { _recordHost.Children.Add(Widgets.EmptyState(_onlyUnacked ? "没有未确认的预警" : "尚无预警记录")); return; }

        var panel = new StackPanel { Spacing = 0 };
        for (int i = 0; i < records.Count; i++)
            panel.Children.Add(RecordRow(records[i], i % 2 == 1));
        _recordHost.Children.Add(panel);

        _recordHost.Children.Add(Widgets.Hint("预警由规则阈值触发，刷新时不重复堆积；同一规则+项目的未确认记录只更新数值。"));
    }

    private Control RecordRow(AlertRecord a, bool alt)
    {
        var danger = a.Severity == KpiSeverity.Danger;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var badge = Widgets.Badge(danger ? "告警" : "预警", danger ? "danger" : "accent");
        badge.VerticalAlignment = VerticalAlignment.Top;
        var text = new StackPanel { Spacing = 3, Margin = new Thickness(10, 0, 10, 0) };
        text.Children.Add(new TextBlock { Text = a.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = danger ? Palette.Red : Palette.Ink });
        text.Children.Add(new TextBlock { Text = a.Message, FontSize = 12, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        text.Children.Add(new TextBlock { Text = a.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), FontSize = 11, Foreground = Palette.Txt3 });
        var right = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(new TextBlock { Text = a.Acknowledged ? "已确认" : "未确认", FontSize = 11.5, Foreground = a.Acknowledged ? Palette.Txt3 : Palette.AccentDeep, HorizontalAlignment = HorizontalAlignment.Right });
        if (!a.Acknowledged)
        {
            var ack = new Button { Content = "确认", FontSize = 11.5 };
            ack.Classes.Add("pill");
            var id = a.Id;
            ack.Click += async (_, _) => { await _alerts.AcknowledgeAsync(id); await ReloadAlertsAsync(); };
            right.Children.Add(ack);
        }
        Grid.SetColumn(badge, 0); Grid.SetColumn(text, 1); Grid.SetColumn(right, 2);
        grid.Children.Add(badge); grid.Children.Add(text); grid.Children.Add(right);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(10, 10, 10, 10),
            Background = alt ? new SolidColorBrush(Color.Parse("#FBF7F0")) : null,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private async Task EvaluateAsync()
    {
        var created = await _alerts.EvaluateAsync();
        await ReloadAllAsync();
        SetMsg(created < 0 ? "无启用的规则可评估" : created == 0 ? "评估完成，无新增预警" : $"评估完成，新增 {created} 条预警");
    }

    // ==================== 规则 ====================
    private async Task ReloadRulesAsync()
    {
        _ruleHost.Children.Clear();
        var rules = await _alerts.ListRulesAsync();
        if (rules.Count == 0) { _ruleHost.Children.Add(Widgets.EmptyState("尚无预警规则，点击「新建规则」开始")); return; }

        var panel = new StackPanel { Spacing = 0 };
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,Auto,Auto,Auto") };
            var name = new TextBlock { Text = r.Name, FontSize = 12.5, Foreground = Palette.Ink, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var metric = new TextBlock { Text = DashboardService.Metric(r.MetricKey)?.Name ?? r.MetricKey, FontSize = 12, Foreground = Palette.Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var cond = new TextBlock { Text = $"{OpLabel(r.Operator)} {r.Threshold:N0}", FontSize = 12.5, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var sev = Widgets.Badge(r.Severity == KpiSeverity.Danger ? "告警" : "预警", r.Severity == KpiSeverity.Danger ? "danger" : "accent");
            sev.VerticalAlignment = VerticalAlignment.Center;
            var edit = new Button { Content = "编辑", FontSize = 11.5 };
            edit.Classes.Add("pill");
            var captured = r;
            edit.Click += (_, _) => OpenRule(captured);
            Grid.SetColumn(name, 0); Grid.SetColumn(metric, 1); Grid.SetColumn(cond, 2); Grid.SetColumn(sev, 3); Grid.SetColumn(edit, 4);
            grid.Children.Add(name); grid.Children.Add(metric); grid.Children.Add(cond); grid.Children.Add(sev); grid.Children.Add(edit);
            panel.Children.Add(new Border
            {
                Child = grid,
                Padding = new Thickness(10, 9, 10, 9),
                Background = i % 2 == 1 ? new SolidColorBrush(Color.Parse("#FBF7F0")) : null,
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(0, 0, 0, 1)
            });
        }
        _ruleHost.Children.Add(panel);
        _ruleHost.Children.Add(Widgets.Hint("指标含经济口径：预计最终成本（预测版本）/预计超目标/未归属金额/未纳入预测的变化/已过期金额；相关文案已标注「非财务结论」。"));
    }

    private void OpenRule(AlertRule? rule)
    {
        var metrics = DashboardService.Catalog.ToList();
        var name = new TextBox { Width = 400, Watermark = "规则名", Text = rule?.Name ?? "新规则" };
        var metric = new ComboBox { ItemsSource = metrics, DisplayMemberBinding = new Binding("Name"), Width = 400, PlaceholderText = "指标" };
        if (rule != null) metric.SelectedItem = metrics.FirstOrDefault(m => m.Key == rule.MetricKey);
        var op = new ComboBox { ItemsSource = Ops.Select(o => o.Label).ToList(), Width = 400, PlaceholderText = "条件", SelectedIndex = rule is null ? 0 : Math.Max(0, Array.FindIndex(Ops, o => o.Value == rule.Operator)) };
        var threshold = new NumericUpDown { Width = 400, FormatString = "N0", Minimum = -99999999, Maximum = 99999999, Value = rule?.Threshold ?? 0m };
        var severity = new ComboBox { ItemsSource = new[] { "预警", "告警" }, Width = 400, SelectedIndex = rule?.Severity == KpiSeverity.Danger ? 1 : 0 };
        var enabled = new CheckBox { Content = "启用该规则", IsChecked = rule?.Enabled ?? true };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("规则名", name, 84));
        body.Children.Add(Widgets.Row("指标", metric, 84));
        body.Children.Add(Widgets.Row("条件", op, 84));
        body.Children.Add(Widgets.Row("阈值", threshold, 84));
        body.Children.Add(Widgets.Row("级别", severity, 84));
        body.Children.Add(enabled);

        Widgets.OpenForm(this, rule is null ? "新建预警规则" : "编辑预警规则", "对某指标在阈值条件下触发；ProjectId 空=全部项目。", body, "保存", async () =>
        {
            if (metric.SelectedItem is not KpiMetric m) return "请选择指标。";
            var entity = rule ?? new AlertRule();
            entity.Name = name.Text?.Trim() ?? "";
            entity.MetricKey = m.Key;
            entity.Operator = Ops[Math.Clamp(op.SelectedIndex, 0, Ops.Length - 1)].Value;
            entity.Threshold = threshold.Value ?? 0m;
            entity.Severity = severity.SelectedIndex == 1 ? KpiSeverity.Danger : KpiSeverity.Warn;
            entity.Enabled = enabled.IsChecked == true;
            try
            {
                await _alerts.SaveRuleAsync(entity);
                await ReloadRulesAsync();
                SetMsg($"已保存规则「{entity.Name}」。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    // ==================== 计划任务 ====================
    private async Task ReloadTasksAsync()
    {
        _taskHost.Children.Clear();
        var tasks = await _scheduler.ListTasksAsync();
        if (tasks.Count == 0) { _taskHost.Children.Add(Widgets.EmptyState("尚无计划任务")); }
        else
        {
            var headers = new[] { "名称", "间隔(分钟)", "最近运行", "产出", "启用", "" };
            var list = tasks.Select(t => new[]
            {
                t.Name,
                t.IntervalMinutes.ToString(),
                t.LastRunAt is null ? "从未运行" : t.LastRunAt.Value.ToLocalTime().ToString("MM-dd HH:mm"),
                t.LastRunCount.ToString(),
                t.Enabled ? "启用" : "停用",
                "编辑"
            });
            _taskHost.Children.Add(Widgets.Table(headers, list));
        }
        var add = Widgets.ActionButton("新建计划任务", () => OpenTask(null));
        _taskHost.Children.Add(add);
    }

    private void OpenTask(ScheduledTask? task)
    {
        var name = new TextBox { Width = 400, Watermark = "任务名", Text = task?.Name ?? "每小时评估预警" };
        var interval = new NumericUpDown { Width = 400, Minimum = 1, Maximum = 10080, Value = task?.IntervalMinutes ?? 60 };
        var enabled = new CheckBox { Content = "启用该任务", IsChecked = task?.Enabled ?? true };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("名称", name, 96));
        body.Children.Add(Widgets.Row("间隔(分钟)", interval, 96));
        body.Children.Add(enabled);
        if (task != null)
        {
            body.Children.Add(Widgets.Hint(task.LastRunAt is null
                ? "从未运行。"
                : $"最近运行 {task.LastRunAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}，产出 {task.LastRunCount} 条预警。"));
            var run = Widgets.PrimaryActionButton("立即运行", async () => { await RunTaskAsync(task.Id); });
            body.Children.Add(run);
        }

        Widgets.OpenForm(this, task is null ? "新建计划任务" : "编辑计划任务", "到期由后台调度器自动运行。", body, "保存", async () =>
        {
            var entity = task ?? new ScheduledTask();
            entity.Name = name.Text?.Trim() ?? "";
            entity.Kind = ScheduledTaskKind.RunAlerts;
            entity.IntervalMinutes = (int)(interval.Value ?? 60);
            entity.Enabled = enabled.IsChecked == true;
            try
            {
                await _scheduler.SaveTaskAsync(entity);
                await ReloadTasksAsync();
                SetMsg("已保存计划任务。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    private async Task RunTaskAsync(int taskId)
    {
        var n = await _scheduler.RunNowAsync(taskId, DateTime.UtcNow);
        await ReloadAllAsync();
        SetMsg(n < 0 ? "无规则可评估" : $"运行完成，产出 {n} 条预警");
    }

    private static string OpLabel(FilterOperator op) => Ops.First(o => o.Value == op).Label;

    private void SetMsg(string text)
    {
        _msg.Text = text;
        _msg.Foreground = Palette.Txt2;
    }
}
