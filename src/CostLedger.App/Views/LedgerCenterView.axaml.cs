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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 台账中心：7 类台账统一为"明细 + 合计"；支持关键字筛选、打印（正式版式）、导出/导入 CSV。
/// </summary>
public partial class LedgerCenterView : UserControl
{
    private sealed record Item(LedgerKind Kind, string Name);

    private static readonly Item[] Items =
    {
        new(LedgerKind.Contract, "合同台账"),
        new(LedgerKind.Receipt, "收款台账"),
        new(LedgerKind.Payment, "付款台账"),
        new(LedgerKind.ChangeOrder, "变更 / 签证台账"),
        new(LedgerKind.Material, "材料收料台账"),
        new(LedgerKind.Invoice, "发票台账"),
        new(LedgerKind.Settlement, "竣工结算台账"),
    };

    private readonly AppDbContext _db = null!;
    private readonly LedgerService _ledger = null!;
    private readonly LedgerImportService _import = null!;
    private int _projectId;
    private string _projectName = "";
    private LedgerResult? _current;
    private List<string[]> _allRows = new();   // 当前台账经关键字筛选后的全量行（分页前）
    private int _page;                         // 当前页（0 起）
    private const int PageSize = 200;          // 每页行数：限制一次性物化的 TextBlock 数量，保障大台账流畅

    public LedgerCenterView()
    {
        InitializeComponent();
    }

    public LedgerCenterView(AppDbContext db) : this()
    {
        _db = db;
        _ledger = new LedgerService(db);
        _import = new LedgerImportService(db);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        ProjCombo.ItemsSource = projects;
        ProjCombo.DisplayMemberBinding = new Binding("Name");

        LedgerList.ItemsSource = Items.ToList();
        LedgerList.DisplayMemberBinding = new Binding("Name");

        ProjCombo.SelectedIndex = projects.Count > 0 ? 0 : -1;
        LedgerList.SelectedIndex = 0;
    }

    private async void OnProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjCombo.SelectedItem is not Project p) return;
        _projectId = p.Id;
        _projectName = p.Name;
        await ReloadAsync();
    }

    private async void OnLedgerSelected(object? sender, SelectionChangedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_projectId == 0 || LedgerList.SelectedItem is not Item it) return;
        _current = await _ledger.BuildAsync(_projectId, it.Kind);
        LedgerTitle.Text = _current.Title;
        RenderCurrent();
    }

    // ---------- 渲染（含关键字筛选 + 分页） ----------
    private void RenderCurrent()
    {
        if (_current is null) return;
        var kw = FilterBox.Text?.Trim() ?? "";
        _allRows = string.IsNullOrEmpty(kw)
            ? _current.Rows.ToList()
            : _current.Rows.Where(r => r.Any(c => c.Contains(kw, StringComparison.OrdinalIgnoreCase))).ToList();
        _page = 0;
        RenderPage();
    }

    private void RenderPage()
    {
        if (_current is null) return;
        var totalPages = _allRows.Count == 0 ? 1 : (int)Math.Ceiling(_allRows.Count / (double)PageSize);
        if (_page < 0) _page = 0;
        if (_page >= totalPages) _page = totalPages - 1;

        var pageRows = _allRows.Skip(_page * PageSize).Take(PageSize).ToList();
        var isLast = _page == totalPages - 1;
        var footers = isLast ? _current.Footers : new List<string[]>();
        Render(new LedgerResult(_current.Title, _current.Headers, pageRows, footers));

        PageLabel.Text = _allRows.Count == 0
            ? "无数据"
            : $"第 {_page + 1} / {totalPages} 页（共 {_allRows.Count} 行，每页 {PageSize}）";
        PrevPage.IsEnabled = _page > 0;
        NextPage.IsEnabled = _page < totalPages - 1;
    }

    private void OnPrevPage(object? sender, RoutedEventArgs e)
    {
        if (_page > 0) { _page--; RenderPage(); }
    }

    private void OnNextPage(object? sender, RoutedEventArgs e)
    {
        _page++; RenderPage();
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e) => RenderCurrent();

    private void Render(LedgerResult r)
    {
        LedgerHost.Children.Clear();
        LedgerHost.ColumnDefinitions.Clear();
        LedgerHost.RowDefinitions.Clear();

        for (int c = 0; c < r.Headers.Count; c++)
            LedgerHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        int totalRows = 1 + r.Rows.Count + r.Footers.Count;
        for (int i = 0; i < totalRows; i++)
            LedgerHost.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < r.Headers.Count; c++)
            LedgerHost.Children.Add(Cell(r.Headers[c], 0, c, header: true, footer: false));

        for (int i = 0; i < r.Rows.Count; i++)
            for (int c = 0; c < r.Headers.Count; c++)
                LedgerHost.Children.Add(Cell(r.Rows[i][c], i + 1, c, header: false, footer: false));

        for (int i = 0; i < r.Footers.Count; i++)
            for (int c = 0; c < r.Headers.Count; c++)
                LedgerHost.Children.Add(Cell(r.Footers[i][c], 1 + r.Rows.Count + i, c, header: false, footer: true));
    }

    private TextBlock Cell(string text, int row, int col, bool header, bool footer)
    {
        var tb = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 0, 26, 8) };
        if (header)
        {
            tb.FontWeight = FontWeight.Bold;
            tb.Foreground = Palette.Find("Txt3Brush");
        }
        else if (footer)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb.Foreground = Palette.AccentDeep;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    // ---------- 打印 ----------
    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var company = await new SystemParamService(_db).GetAsync("公司名称");
        PrintService.OpenInBrowser(PrintService.BuildHtml(_projectName, _current, DateTime.Now, company), _current.Title);
    }

    // ---------- 导出 CSV ----------
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var all = new List<string[]>();
        all.AddRange(_current.Rows);
        all.AddRange(_current.Footers);
        var csv = CsvUtil.Write(_current.Headers, all);

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"{_current.Title}_{_projectName}.csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true)); // BOM：Excel 中文不乱码
        await writer.WriteAsync(csv);
        StatusText.Text = $"已导出 CSV（Excel 可直接打开）";
    }

    // ---------- 导入 CSV ----------
    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_projectId == 0 || LedgerList.SelectedItem is not Item it) return;
        if (it.Kind is not (LedgerKind.ChangeOrder or LedgerKind.Material or LedgerKind.Receipt or LedgerKind.Payment))
        {
            StatusText.Text = "该台账暂不支持导入（支持：变更签证 / 材料 / 收款 / 付款）";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        var res = await _import.ImportAsync(_projectId, it.Kind, text);
        StatusText.Text = $"导入完成：成功 {res.Created}，跳过 {res.Skipped}"
            + (res.Errors.Count > 0 ? $"；首条错误：{res.Errors[0]}" : "");
        await ReloadAsync();
    }
}
