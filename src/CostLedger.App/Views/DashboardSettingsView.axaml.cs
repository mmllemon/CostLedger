using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// M2-D 看板设计器：配置 KPI 卡片的显隐 / 标题 / 顺序 / 阈值。
/// 配置存入 DashboardWidget（数据），成本看板据此渲染，无需改代码。
/// </summary>
public partial class DashboardSettingsView : UserControl
{
    private readonly CostLedger.Infrastructure.DashboardService _dashboard = null!;
    private readonly List<Row> _rows = new();

    private sealed record Row(string Key, CheckBox Visible, TextBox Title, NumericUpDown Order, TextBox Warn, TextBox Danger);

    public DashboardSettingsView()
    {
        InitializeComponent();
    }

    public DashboardSettingsView(CostLedger.Infrastructure.DashboardService dashboard) : this()
    {
        _dashboard = dashboard;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var existing = (await _dashboard.ListWidgetsAsync()).ToDictionary(w => w.WidgetKey);
        int i = 0;
        foreach (var m in CostLedger.Infrastructure.DashboardService.Catalog)
        {
            i++;
            existing.TryGetValue(m.Key, out var w);
            var chk = new CheckBox { IsChecked = w?.Visible ?? false, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBox { Text = string.IsNullOrWhiteSpace(w?.Title) ? m.Name : w!.Title };
            var order = new NumericUpDown { Value = w?.DisplayOrder ?? i, Minimum = 0, Maximum = 9999, Width = 56 };
            var warn = new TextBox { Text = w?.WarnThreshold?.ToString(CultureInfo.InvariantCulture) ?? "", Width = 110 };
            var danger = new TextBox { Text = w?.DangerThreshold?.ToString(CultureInfo.InvariantCulture) ?? "", Width = 110 };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,56,110,110") };
            Add(grid, chk, 0);
            Add(grid, title, 1);
            Add(grid, order, 2);
            Add(grid, warn, 3);
            Add(grid, danger, 4);
            RowsHost.Children.Add(grid);

            _rows.Add(new Row(m.Key, chk, title, order, warn, danger));
        }
    }

    private static void Add(Grid g, Control c, int col)
    {
        c.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var list = _rows.Select(r => new CostLedger.Domain.DashboardWidget
        {
            WidgetKey = r.Key,
            Title = r.Title.Text?.Trim() ?? "",
            Visible = r.Visible.IsChecked == true,
            DisplayOrder = (int)(r.Order.Value ?? 0),
            WarnThreshold = Parse(r.Warn.Text),
            DangerThreshold = Parse(r.Danger.Text)
        }).ToList();

        await _dashboard.SaveWidgetsAsync(list);
        StatusText.Text = "✓ 已保存，切换「成本看板」查看效果";
    }

    private static decimal? Parse(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
}
