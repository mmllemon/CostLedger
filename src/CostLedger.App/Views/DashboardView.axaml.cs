using Avalonia.Controls;
using Avalonia.Interactivity;
using CostLedger.Domain;
using CostLedger.App.ViewModels;
using System;
using System.Linq;

namespace CostLedger.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not DashboardViewModel vm) return;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DashboardViewModel.Lines)) RenderSpineChart(vm);
        };
    }

    /// <summary>阶段3 图表：成本脊柱按科目分组柱状图（目标/已发生/预计最终），几何复用 ChartLayout。</summary>
    private void RenderSpineChart(DashboardViewModel vm)
    {
        var lines = vm.Lines.ToList();
        if (lines.Count == 0) { SpineChartHost.Content = null; return; }

        var model = new ChartModel
        {
            Title = "目标 / 已发生 / 预计最终（按成本科目）",
            Categories = lines.Select(l => Trunc(l.CostCategoryName, 8)).ToList(),
            Series = new[]
            {
                new ChartSeries("目标", lines.Select(l => l.Target).ToList(), "#8A8578"),
                new ChartSeries("已发生", lines.Select(l => l.Incurred).ToList(), "#CC785C"),
                new ChartSeries("预计最终", lines.Select(l => l.EstimatedFinal).ToList(), "#A24E30"),
            }
        };
        SpineChartHost.Content = ChartControl.Build(model, ChartKind.Bar);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
