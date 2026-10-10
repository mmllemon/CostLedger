using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// M2-C 报表中心：报表定义（数据源 + 列配置 + 筛选）可视化配置 + 运行预览。
/// 定义全部作为"数据"存储（ReportDef/Column/Filter），后台配置即生效，无需改代码。
/// </summary>
public partial class ReportCenterView : UserControl
{
    private static readonly string[] AggLabels = { "无", "求和", "平均", "计数", "最小", "最大" };
    private static readonly string[] OpLabels = { "包含", "等于", "不等于", "大于", "小于", "大于等于", "小于等于" };

    private readonly ReportService _reports = null!;

    private readonly List<ColumnRow> _columnRows = new();
    private readonly List<FilterRow> _filterRows = new();
    private int _currentId;
    private string? _currentCode;
    private bool _loading;

    // 阶段3 图表视图
    private static readonly string[] ViewModes = { "表格", "柱状图", "折线图", "饼图" };
    private ReportResult? _last;   // 最近一次运行结果（表格/图表共用）

    private sealed record ColumnRow(ReportFieldDef Field, CheckBox Visible, TextBox Label, NumericUpDown Order, TextBox Format, ComboBox Agg);
    private sealed record FilterRow(ComboBox Field, ComboBox Op, TextBox Value, Control Container);

    public ReportCenterView()
    {
        InitializeComponent();
    }

    public ReportCenterView(ReportService reports) : this()
    {
        _reports = reports;
        ViewMode.ItemsSource = ViewModes;
        ViewMode.SelectedIndex = 0;
        ExportSvgBtn.IsEnabled = false;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        SourceCombo.ItemsSource = ReportDataSources.All.Values.ToList();
        SourceCombo.DisplayMemberBinding = new Binding("Name");
        await ReloadListAsync();
    }

    private async Task ReloadListAsync(int selectId = 0)
    {
        var defs = await _reports.ListReportsAsync();
        ReportList.ItemsSource = defs;
        ReportList.DisplayMemberBinding = new Binding("Name");
        if (selectId > 0)
            ReportList.SelectedItem = defs.FirstOrDefault(d => d.Id == selectId);
        else if (defs.Count > 0)
            ReportList.SelectedIndex = 0;
    }

    // ---------- 选中 / 新建 ----------
    private void OnReportSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ReportList.SelectedItem is not ReportDef def) return;
        _loading = true;
        _currentId = def.Id;
        _currentCode = def.Code;
        NameBox.Text = def.Name;
        TitleBox.Text = def.Title;
        SourceCombo.SelectedItem = ReportDataSources.Get(def.DataSourceCode) ?? ReportDataSources.All.Values.First();
        _loading = false;

        BuildColumns(CurrentSource, def);
        BuildFilters(def.Filters.ToList());
    }

    private void OnNewReport(object? sender, RoutedEventArgs e)
    {
        _loading = true;
        _currentId = 0;
        _currentCode = null;
        NameBox.Text = "新报表";
        TitleBox.Text = "新报表";
        SourceCombo.SelectedItem = ReportDataSources.All.Values.First();
        _loading = false;
        ReportList.SelectedItem = null;
        BuildColumns(CurrentSource, null);
        BuildFilters(new List<ReportFilter>());
        ResultTitle.Text = "运行结果";
        ResultHost.Children.Clear();
        _last = null;
        ExportSvgBtn.IsEnabled = false;
    }

    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        BuildColumns(CurrentSource, null);
    }

    private ReportDataSource CurrentSource =>
        SourceCombo.SelectedItem as ReportDataSource ?? ReportDataSources.All.Values.First();

    // ---------- 列配置 ----------
    private void BuildColumns(ReportDataSource source, ReportDef? def)
    {
        ColumnsHost.Children.Clear();
        _columnRows.Clear();
        var fields = source.Fields;
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var existing = def?.Columns.FirstOrDefault(c => c.FieldKey == field.Key);
            bool visible = existing?.Visible ?? false;
            string label = existing?.Label is { Length: > 0 } l ? l : field.Label;
            int order = existing?.DisplayOrder ?? (i + 1);
            string format = existing?.Format ?? field.Format ?? "";
            var agg = existing?.Aggregate ?? AggregateType.None;

            var chk = new CheckBox { IsChecked = visible, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBox { Text = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            var ord = new NumericUpDown { Value = order, Minimum = 0, Maximum = 9999, Width = 56 };
            var fmt = new TextBox { Text = format, Width = 90 };
            var aggBox = new ComboBox { ItemsSource = AggLabels, SelectedIndex = (int)agg, Width = 120 };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,56,90,120") };
            grid.Children.Add(Place(chk, 0));
            grid.Children.Add(Place(lbl, 1));
            grid.Children.Add(Place(ord, 2));
            grid.Children.Add(Place(fmt, 3));
            grid.Children.Add(Place(aggBox, 4));
            ColumnsHost.Children.Add(grid);

            _columnRows.Add(new ColumnRow(field, chk, lbl, ord, fmt, aggBox));
        }
    }

    private static Control Place(Control c, int col)
    {
        c.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(c, col);
        return c;
    }

    // ---------- 筛选 ----------
    private void BuildFilters(List<ReportFilter> filters)
    {
        FiltersHost.Children.Clear();
        _filterRows.Clear();
        foreach (var f in filters)
            AddFilterRow(f);
    }

    private void OnAddFilter(object? sender, RoutedEventArgs e) => AddFilterRow(null);

    private void AddFilterRow(ReportFilter? preset)
    {
        var source = CurrentSource;
        var fieldCombo = new ComboBox { ItemsSource = source.Fields.ToList(), DisplayMemberBinding = new Binding("Label"), Width = 150 };
        fieldCombo.SelectedItem = source.Fields.FirstOrDefault(x => x.Key == preset?.FieldKey) ?? source.Fields.FirstOrDefault();
        var opCombo = new ComboBox { ItemsSource = OpLabels, SelectedIndex = (int)(preset?.Operator ?? FilterOperator.Contains), Width = 110 };
        var valBox = new TextBox { Text = preset?.Value ?? "", Width = 180 };
        var del = new Button { Content = "✕", Classes = { "icon" } };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,110,180,Auto") };
        row.Children.Add(Place(fieldCombo, 0));
        row.Children.Add(Place(opCombo, 1));
        row.Children.Add(Place(valBox, 2));
        Grid.SetColumn(del, 3);
        row.Children.Add(del);

        var entry = new FilterRow(fieldCombo, opCombo, valBox, row);
        del.Click += (_, _) =>
        {
            FiltersHost.Children.Remove(row);
            _filterRows.Remove(entry);
        };

        FiltersHost.Children.Add(row);
        _filterRows.Add(entry);
    }

    // ---------- 保存 / 运行 ----------
    private ReportDef BuildDefFromUi()
    {
        var source = CurrentSource;
        var def = new ReportDef
        {
            Id = _currentId,
            Code = string.IsNullOrWhiteSpace(_currentCode) ? $"RPT-{DateTime.Now:yyyyMMddHHmmss}" : _currentCode!,
            Name = NameBox.Text?.Trim() ?? "",
            Title = TitleBox.Text?.Trim() ?? "",
            DataSourceCode = source.Code
        };

        int order = 1;
        foreach (var r in _columnRows.Where(r => r.Visible.IsChecked == true))
        {
            def.Columns.Add(new ReportColumn
            {
                FieldKey = r.Field.Key,
                Label = r.Label.Text?.Trim() ?? r.Field.Label,
                Visible = true,
                DisplayOrder = (int)(r.Order.Value ?? order),
                Format = string.IsNullOrWhiteSpace(r.Format.Text) ? null : r.Format.Text!.Trim(),
                Aggregate = (AggregateType)Math.Clamp(r.Agg.SelectedIndex, 0, 5)
            });
            order++;
        }

        int fOrder = 1;
        foreach (var r in _filterRows)
        {
            if (r.Field.SelectedItem is not ReportFieldDef f) continue;
            if (string.IsNullOrWhiteSpace(r.Value.Text)) continue;
            def.Filters.Add(new ReportFilter
            {
                FieldKey = f.Key,
                Operator = (FilterOperator)Math.Clamp(r.Op.SelectedIndex, 0, 6),
                Value = r.Value.Text!.Trim(),
                DisplayOrder = fOrder++
            });
        }
        return def;
    }

    private async void OnSaveReport(object? sender, RoutedEventArgs e)
    {
        var def = BuildDefFromUi();
        if (string.IsNullOrWhiteSpace(def.Name))
        {
            ResultTitle.Text = "⚠ 请先填写报表名称";
            return;
        }
        var id = await _reports.SaveReportAsync(def);
        _currentId = id;
        _currentCode = def.Code;
        await ReloadListAsync(id);
        ResultTitle.Text = $"✓ 已保存：{def.Name}";
    }

    private void OnRunReport(object? sender, RoutedEventArgs e)
    {
        var def = BuildDefFromUi();
        var result = _reports.Run(def);
        _last = result;
        PopulateChartColumns(result);
        RenderCurrent();
    }

    private async void OnDeleteReport(object? sender, RoutedEventArgs e)
    {
        if (_currentId == 0) return;
        await _reports.DeleteReportAsync(_currentId);
        _currentId = 0;
        _currentCode = null;
        await ReloadListAsync();
    }

    // ---------- 预览渲染 ----------
    private void RenderResult(ReportResult r)
    {
        ResultTitle.Text = string.IsNullOrWhiteSpace(r.Title) ? "运行结果" : r.Title;
        ResultHost.Children.Clear();
        ResultHost.ColumnDefinitions.Clear();
        ResultHost.RowDefinitions.Clear();

        if (r.Headers.Count == 0)
        {
            ResultHost.Children.Add(new TextBlock
            {
                Text = "（无可输出的列。请在上方勾选至少一列后运行。）",
                Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")!,
                FontSize = 12
            });
            return;
        }

        for (int c = 0; c < r.Headers.Count; c++)
            ResultHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        int rowCount = 1 + r.Rows.Count + (r.Footers.Any(x => !string.IsNullOrEmpty(x)) ? 1 : 0);
        for (int i = 0; i < rowCount; i++)
            ResultHost.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < r.Headers.Count; c++)
            ResultHost.Children.Add(Cell(r.Headers[c], 0, c, header: true));

        for (int i = 0; i < r.Rows.Count; i++)
            for (int c = 0; c < r.Headers.Count; c++)
                ResultHost.Children.Add(Cell(r.Rows[i][c], i + 1, c, header: false));

        if (r.Footers.Any(x => !string.IsNullOrEmpty(x)))
        {
            int fr = 1 + r.Rows.Count;
            for (int c = 0; c < r.Headers.Count; c++)
                ResultHost.Children.Add(Cell(r.Footers[c], fr, c, header: false, footer: true));
        }
    }

    private TextBlock Cell(string text, int row, int col, bool header, bool footer = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 22, 8)
        };
        if (header) tb.FontWeight = FontWeight.Bold;
        if (footer)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb.Foreground = (SolidColorBrush)this.FindResource("AccentDeepBrush")!;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    // ---------- 阶段3 图表视图 ----------
    private void PopulateChartColumns(ReportResult r)
    {
        CatCombo.ItemsSource = r.Headers.ToList();
        ValCombo.ItemsSource = r.Headers.ToList();

        // 自动猜测：数值列=首个过半可解析为数字的列；类别列=首个非数值列（退而求其次取第 0 列）
        bool NumericCol(int i) => r.Rows.Count > 0
            && r.Rows.Count(row => row.Length > i && decimal.TryParse(row[i], NumberStyles.Any, CultureInfo.InvariantCulture, out _)) * 2 >= r.Rows.Count;
        int valIdx = Enumerable.Range(0, r.Headers.Count).FirstOrDefault(NumericCol, -1);
        int catIdx = Enumerable.Range(0, r.Headers.Count).FirstOrDefault(i => i != valIdx && !NumericCol(i), valIdx == 0 ? Math.Min(1, r.Headers.Count - 1) : 0);

        CatCombo.SelectedIndex = r.Headers.Count > 0 ? Math.Clamp(catIdx, 0, r.Headers.Count - 1) : -1;
        ValCombo.SelectedIndex = valIdx >= 0 ? valIdx : Math.Min(1, r.Headers.Count - 1);
        ExportSvgBtn.IsEnabled = r.Headers.Count > 1 && r.Rows.Count > 0;
    }

    private void OnViewModeChanged(object? sender, SelectionChangedEventArgs e) => RenderCurrent();

    private void OnChartColChanged(object? sender, SelectionChangedEventArgs e) => RenderCurrent();

    private void RenderCurrent()
    {
        if (_last is null) return;
        if (ViewMode.SelectedIndex <= 0)
        {
            RenderResult(_last);
            return;
        }
        var (model, kind) = BuildChartModel(_last);
        if (model is null) return;

        ResultHost.Children.Clear();
        ResultHost.ColumnDefinitions.Clear();
        ResultHost.RowDefinitions.Clear();
        var canvas = ChartControl.Build(model, kind);
        canvas.HorizontalAlignment = HorizontalAlignment.Left;
        ResultHost.Children.Add(canvas);
    }

    private (ChartModel?, ChartKind) BuildChartModel(ReportResult r)
    {
        int ci = CatCombo.SelectedIndex, vi = ValCombo.SelectedIndex;
        if (ci < 0 || vi < 0 || ci >= r.Headers.Count || vi >= r.Headers.Count)
            return (null, ChartKind.Bar);

        var kind = ViewMode.SelectedIndex switch { 1 => ChartKind.Bar, 2 => ChartKind.Line, _ => ChartKind.Pie };
        var categories = r.Rows.Select(row => row.Length > ci ? row[ci] : "").ToList();
        var values = r.Rows.Select(row => row.Length > vi && decimal.TryParse(row[vi], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m).ToList();
        if (categories.Count == 0) return (null, kind);

        var model = new ChartModel
        {
            Title = string.IsNullOrWhiteSpace(r.Title) ? "图表" : r.Title,
            Categories = categories,
            Series = new[] { new ChartSeries(r.Headers[vi], values, ChartModel.DefaultPalette[0]) }
        };
        return (model, kind);
    }

    private async void OnExportSvg(object? sender, RoutedEventArgs e)
    {
        if (_last is null || ViewMode.SelectedIndex <= 0) return;
        var (model, kind) = BuildChartModel(_last);
        if (model is null) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            SuggestedFileName = $"{(string.IsNullOrWhiteSpace(model.Title) ? "chart" : model.Title)}.svg",
            FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("SVG") { Patterns = new[] { "*.svg" } } }
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
        await writer.WriteAsync(ChartSvg.Build(model, kind));
    }
}
