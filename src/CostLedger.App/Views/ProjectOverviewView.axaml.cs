using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 项目总览：一屏给出项目的钱与成本全貌（合同/收付/变更 + 六类成本 + 毛利）。
/// 可"打印项目总览"（正式版式）。
/// </summary>
public partial class ProjectOverviewView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly ProjectOverviewService _overview = null!;
    private readonly AlertService _alerts = null!;
    private ProjectOverview? _current;

    public ProjectOverviewView()
    {
        InitializeComponent();
    }

    public ProjectOverviewView(AppDbContext db, AlertService alerts) : this()
    {
        _db = db;
        _overview = new ProjectOverviewService(db, new CostSpineService(db));
        _alerts = alerts;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        ProjCombo.ItemsSource = projects;
        ProjCombo.DisplayMemberBinding = new Binding("Name");
        ProjCombo.SelectedIndex = projects.Count > 0 ? 0 : -1;
    }

    private async void OnProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjCombo.SelectedItem is not Project p) return;
        _current = await _overview.BuildAsync(p.Id);
        Render(_current);
        await RenderAlertsAsync(p.Id);
    }

    /// <summary>待办提醒：该项目未确认的预警（按级别着色）。</summary>
    private async Task RenderAlertsAsync(int projectId)
    {
        AlertHost.Children.Clear();
        var list = (await _alerts.ListAlertsAsync(onlyUnacknowledged: true))
            .Where(a => a.ProjectId == projectId).ToList();

        if (list.Count == 0)
        {
            AlertHost.Children.Add(new TextBlock
            {
                Text = "暂无未确认预警",
                Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")!
            });
            return;
        }

        foreach (var a in list)
        {
            var brush = (SolidColorBrush)this.FindResource(
                a.Severity == KpiSeverity.Danger ? "RedBrush" : "AccentDeepBrush")!;
            AlertHost.Children.Add(new TextBlock
            {
                Text = "• " + a.Message,
                Foreground = brush,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void Render(ProjectOverview o)
    {
        MoneyHost.Children.Clear();
        CostHost.Children.Clear();

        void Add(WrapPanel host, string label, decimal v, bool bad = false) => host.Children.Add(Card(label, Money(v), bad));

        Add(MoneyHost, "对上合同额", o.UpstreamContract);
        Add(MoneyHost, "对下合同额", o.DownstreamContract);
        Add(MoneyHost, "累计收款", o.ReceivedCumulative);
        Add(MoneyHost, "未收", o.Receivable);
        Add(MoneyHost, "累计付款", o.PaidCumulative);
        Add(MoneyHost, "未付", o.Payable);
        Add(MoneyHost, "变更(+)", o.ChangePositive);
        Add(MoneyHost, "变更(−)", o.ChangeNegative);
        Add(MoneyHost, "变更净额", o.ChangeNet);

        Add(CostHost, "目标成本", o.Target);
        Add(CostHost, "已承诺", o.Committed);
        Add(CostHost, "已发生", o.Incurred);
        Add(CostHost, "已完未结", o.WorkDoneNotSettled);
        Add(CostHost, "剩余", o.Remaining, bad: o.Remaining < 0);
        Add(CostHost, "预计最终", o.EstimatedFinal);
        Add(CostHost, "毛利(对上−预计最终)", o.GrossProfit, bad: o.GrossProfit < 0);
    }

    private Border Card(string label, string value, bool bad)
    {
        var v = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6, 0, 0)
        };
        if (bad) v.Foreground = (SolidColorBrush)this.FindResource("RedBrush")!;

        return new Border
        {
            Classes = { "sidecard" },
            Width = 200,
            Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 11, Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")! },
                    v
                }
            }
        };
    }

    private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var o = _current;
        var headers = new[] { "指标", "数值" };
        var rows = new List<string[]>
        {
            new[] { "对上合同额", Money(o.UpstreamContract) },
            new[] { "对下合同额", Money(o.DownstreamContract) },
            new[] { "累计收款", Money(o.ReceivedCumulative) },
            new[] { "未收", Money(o.Receivable) },
            new[] { "累计付款", Money(o.PaidCumulative) },
            new[] { "未付", Money(o.Payable) },
            new[] { "变更(+)", Money(o.ChangePositive) },
            new[] { "变更(−)", Money(o.ChangeNegative) },
            new[] { "变更净额", Money(o.ChangeNet) },
            new[] { "目标成本", Money(o.Target) },
            new[] { "已承诺", Money(o.Committed) },
            new[] { "已发生", Money(o.Incurred) },
            new[] { "已完未结", Money(o.WorkDoneNotSettled) },
            new[] { "剩余", Money(o.Remaining) },
            new[] { "预计最终", Money(o.EstimatedFinal) },
            new[] { "毛利（对上−预计最终）", Money(o.GrossProfit) },
        };
        var ledger = new LedgerResult("项目总览", headers, rows, Array.Empty<string[]>());
        PrintService.OpenInBrowser(PrintService.BuildHtml(o.ProjectName, ledger, DateTime.Now), "项目总览");
    }
}
