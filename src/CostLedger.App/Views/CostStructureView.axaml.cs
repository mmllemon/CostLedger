using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 成本结构：部位（分部分项）+ 工程量清单（目标成本）维护；以及"按部位成本"（定位超支）。
/// </summary>
public partial class CostStructureView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly WbsService _wbs = null!;
    private readonly BoqService _boq = null!;
    private readonly LedgerService _ledger = null!;
    private readonly LedgerImportService _import = null!;

    private List<CostCategory> _categories = new();
    private List<WBSItem> _wbsItems = new();
    private int _projectId;
    private string _projectName = "";
    private int _wbsId, _boqId;

    public CostStructureView()
    {
        InitializeComponent();
    }

    public CostStructureView(AppDbContext db) : this()
    {
        _db = db;
        _wbs = new WbsService(db);
        _boq = new BoqService(db);
        _ledger = new LedgerService(db);
        _import = new LedgerImportService(db);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        ProjCombo.ItemsSource = projects;
        ProjCombo.DisplayMemberBinding = new Binding("Name");
        ProjCombo.SelectedIndex = projects.Count > 0 ? 0 : -1;

        _categories = await _db.CostCategories.OrderBy(c => c.Name).ToListAsync();
        BoqCategory.ItemsSource = _categories;
        BoqCategory.DisplayMemberBinding = new Binding("Name");
    }

    private async void OnProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjCombo.SelectedItem is not Project p) return;
        _projectId = p.Id;
        _projectName = p.Name;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _wbsItems = await _wbs.ListAsync(_projectId);
        WbsList.ItemsSource = _wbsItems;
        WbsList.DisplayMemberBinding = new Binding("FullPath");
        WbsParent.ItemsSource = _wbsItems;
        WbsParent.DisplayMemberBinding = new Binding("FullPath");
        BoqWbs.ItemsSource = _wbsItems;
        BoqWbs.DisplayMemberBinding = new Binding("FullPath");
        await ReloadBoqAsync();
        await LoadWbsCostAsync();
    }

    // ==================== 部位 ====================
    private void OnWbsSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (WbsList.SelectedItem is not WBSItem w) return;
        _wbsId = w.Id;
        WbsCode.Text = w.Code;
        WbsName.Text = w.Name;
        WbsParent.SelectedItem = _wbsItems.FirstOrDefault(x => x.Id == w.ParentId);
    }

    private void OnWbsNew(object? sender, RoutedEventArgs e)
    {
        _wbsId = 0;
        WbsCode.Text = ""; WbsName.Text = ""; WbsParent.SelectedItem = null;
        WbsList.SelectedItem = null;
    }

    private async void OnWbsSave(object? sender, RoutedEventArgs e)
    {
        var w = _wbsId > 0 ? _wbsItems.First(x => x.Id == _wbsId) : new WBSItem { ProjectId = _projectId };
        w.Code = WbsCode.Text?.Trim() ?? "";
        w.Name = WbsName.Text?.Trim() ?? "";
        w.ParentId = (WbsParent.SelectedItem as WBSItem)?.Id;
        var id = await _wbs.SaveAsync(w);
        await RefreshAsync();
        WbsList.SelectedItem = _wbsItems.FirstOrDefault(x => x.Id == id);
    }

    private async void OnWbsDelete(object? sender, RoutedEventArgs e)
    {
        if (_wbsId == 0) return;
        await _wbs.DeleteAsync(_wbsId);
        _wbsId = 0;
        await RefreshAsync();
        OnWbsNew(null, new RoutedEventArgs());
    }

    // ==================== 清单 ====================
    private async Task ReloadBoqAsync(int selectId = 0)
    {
        var list = await _boq.ListAsync(_projectId);
        BoqList.ItemsSource = list;
        BoqList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
        BoqTotal.Text = "清单合价（目标成本）：" + (await _boq.TotalAsync(_projectId)).ToString("N2", CultureInfo.CurrentCulture);
    }

    private void OnBoqSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (BoqList.SelectedItem is not BoqLine b) return;
        _boqId = b.Id;
        BoqCode.Text = b.Code;
        BoqName.Text = b.ItemName;
        BoqFeature.Text = b.Feature;
        BoqUnit.Text = b.Unit;
        BoqQty.Value = b.Quantity;
        BoqCompleted.Value = b.CompletedQuantity;
        BoqPrice.Value = b.UnitPrice;
        BoqAmount.Value = b.Amount;
        BoqTax.Value = b.TaxRate;
        UpdateBoqProgress();
        BoqCategory.SelectedItem = _categories.FirstOrDefault(c => c.Id == b.CostCategoryId);
        BoqWbs.SelectedItem = _wbsItems.FirstOrDefault(w => w.Id == b.WbsItemId);
    }

    private void OnBoqNew(object? sender, RoutedEventArgs e)
    {
        _boqId = 0;
        BoqCode.Text = ""; BoqName.Text = ""; BoqFeature.Text = ""; BoqUnit.Text = "";
        BoqQty.Value = 0; BoqCompleted.Value = 0; BoqPrice.Value = 0; BoqAmount.Value = 0; BoqTax.Value = 0.09m;
        BoqProgress.Text = "";
        BoqCategory.SelectedItem = null; BoqWbs.SelectedItem = null;
        BoqList.SelectedItem = null;
    }

    private async void OnBoqSave(object? sender, RoutedEventArgs e)
    {
        var b = _boqId > 0 ? (await _boq.ListAsync(_projectId)).First(x => x.Id == _boqId) : new BoqLine { ProjectId = _projectId };
        b.Code = BoqCode.Text?.Trim() ?? "";
        b.ItemName = BoqName.Text?.Trim() ?? "";
        b.Feature = BoqFeature.Text;
        b.Unit = BoqUnit.Text;
        b.Quantity = BoqQty.Value ?? 0m;
        b.CompletedQuantity = BoqCompleted.Value ?? 0m;
        b.UnitPrice = BoqPrice.Value ?? 0m;
        b.Amount = BoqAmount.Value ?? 0m;
        b.TaxRate = BoqTax.Value ?? 0m;
        b.CostCategoryId = (BoqCategory.SelectedItem as CostCategory)?.Id;
        b.WbsItemId = (BoqWbs.SelectedItem as WBSItem)?.Id;
        var id = await _boq.SaveAsync(b);
        UpdateBoqProgress();
        await ReloadBoqAsync(id);
        await LoadWbsCostAsync();
    }

    private async void OnBoqDelete(object? sender, RoutedEventArgs e)
    {
        if (_boqId == 0) return;
        await _boq.DeleteAsync(_boqId);
        _boqId = 0;
        await ReloadBoqAsync();
        await LoadWbsCostAsync();
        OnBoqNew(null, new RoutedEventArgs());
    }

    // ==================== 清单 导入/导出 ====================
    private async void OnBoqExport(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var r = await _ledger.BuildAsync(_projectId, LedgerKind.Boq);
        var all = new List<string[]>();
        all.AddRange(r.Rows);
        all.AddRange(r.Footers);
        var csv = CsvUtil.Write(r.Headers, all);

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"工程量清单_{_projectName}.csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;
        await using var s = await file.OpenWriteAsync();
        await using var w = new StreamWriter(s, new UTF8Encoding(true));
        await w.WriteAsync(csv);
    }

    private async void OnBoqImport(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (files.Count == 0) return;
        await using var s = await files[0].OpenReadAsync();
        using var reader = new StreamReader(s, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        var res = await _import.ImportAsync(_projectId, LedgerKind.Boq, text);
        await ReloadBoqAsync();
        await LoadWbsCostAsync();
        BoqTotal.Text = $"已导入 {res.Created} 条" + (res.Skipped > 0 ? $"，跳过 {res.Skipped}" : "")
                        + "　｜　清单合价：" + (await _boq.TotalAsync(_projectId)).ToString("N2", CultureInfo.CurrentCulture);
    }

    // ==================== 按部位成本（含完工预测） ====================
    private void UpdateBoqProgress()
    {
        var amt = BoqAmount.Value ?? 0m;
        var done = (BoqCompleted.Value ?? 0m) * (BoqPrice.Value ?? 0m);
        BoqProgress.Text = amt <= 0 ? "—" : (done / amt).ToString("P0", CultureInfo.CurrentCulture);
    }

    private async Task LoadWbsCostAsync()
    {
        var lines = await new CostSpineService(_db).GetSpineByWbsAsync(_projectId);
        WbsCostHost.Children.Clear();
        WbsCostHost.ColumnDefinitions.Clear();
        WbsCostHost.RowDefinitions.Clear();

        var headers = new[] { "部位", "目标(清单)", "已完金额", "完工%", "已发生", "偏差", "预计最终" };
        for (int c = 0; c < headers.Length; c++)
            WbsCostHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < lines.Count + 1; i++)
            WbsCostHost.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < headers.Length; c++) WbsCostHost.Children.Add(Cell(headers[c], 0, c, head: true, red: false));
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var cells = new[]
            {
                l.FullPath, Money(l.Target), Money(l.CompletedAmount),
                l.Target == 0 ? "—" : l.Progress.ToString("P0", CultureInfo.CurrentCulture),
                Money(l.Incurred), Money(l.Variance), Money(l.EstimatedFinal)
            };
            for (int c = 0; c < headers.Length; c++)
                WbsCostHost.Children.Add(Cell(cells[c], i + 1, c, head: false, red: l.IsOver && c == 5));
        }

        // 汇总 + 预计毛利率（对上合同额为基数）
        var upstream = await _db.Contracts.Where(c => c.ProjectId == _projectId && c.TypeCode == "Main").SumAsync(c => c.Amount);
        var eacTotal = lines.Sum(l => l.EstimatedFinal);
        var margin = upstream == 0 ? 0m : (upstream - eacTotal) / upstream;
        WbsCostSummary.Text = $"目标合计 {Money(lines.Sum(l => l.Target))}　已发生合计 {Money(lines.Sum(l => l.Incurred))}　"
            + $"预计最终合计 {Money(eacTotal)}　｜　预计毛利率 {(upstream == 0 ? "—" : margin.ToString("P1", CultureInfo.CurrentCulture))}（对上合同额 {Money(upstream)}）";
    }

    private TextBlock Cell(string text, int row, int col, bool head, bool red)
    {
        var tb = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 0, 26, 8) };
        if (head)
        {
            tb.FontWeight = FontWeight.Bold;
            tb.Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")!;
        }
        else if (red)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb.Foreground = (SolidColorBrush)this.FindResource("RedBrush")!;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    private async void OnRefreshWbsCost(object? sender, RoutedEventArgs e) => await LoadWbsCostAsync();

    private void OnPrintWbsCost(object? sender, RoutedEventArgs e)
    {
        _ = PrintWbsCostAsync();
    }

    private async Task PrintWbsCostAsync()
    {
        var lines = await new CostSpineService(_db).GetSpineByWbsAsync(_projectId);
        var headers = new[] { "部位", "目标(清单)", "已完金额", "完工%", "已发生", "偏差", "预计最终" };
        var rows = lines.Select(l => new[]
        {
            l.FullPath, Money(l.Target), Money(l.CompletedAmount),
            l.Target == 0 ? "—" : l.Progress.ToString("P0", CultureInfo.CurrentCulture),
            Money(l.Incurred), Money(l.Variance), Money(l.EstimatedFinal)
        }).ToList();
        var ledger = new LedgerResult("按部位成本", headers, rows, Array.Empty<string[]>());
        PrintService.OpenInBrowser(PrintService.BuildHtml(_projectName, ledger, DateTime.Now), "按部位成本");
    }

    private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);
}
