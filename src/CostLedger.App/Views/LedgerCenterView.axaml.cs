using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 台账中心：7 类台账（合同/收款/付款/变更签证/材料/发票/结算）统一为"明细 + 合计"视图；
/// 每张台账可"打印此台账"（正式版式）。所有台账复用 LedgerService，结果与打印共用。
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
    private int _projectId;
    private LedgerResult? _current;
    private string _projectName = "";

    public LedgerCenterView()
    {
        InitializeComponent();
    }

    public LedgerCenterView(AppDbContext db) : this()
    {
        _db = db;
        _ledger = new LedgerService(db);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        ProjCombo.ItemsSource = projects;
        ProjCombo.DisplayMemberBinding = new Binding("Name");
        ProjCombo.SelectedIndex = projects.Count > 0 ? 0 : -1;

        LedgerList.ItemsSource = Items.ToList();
        LedgerList.DisplayMemberBinding = new Binding("Name");
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
        Render(_current);
    }

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
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 26, 8)
        };
        if (header)
        {
            tb.FontWeight = FontWeight.Bold;
            tb.Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")!;
        }
        else if (footer)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb.Foreground = (SolidColorBrush)this.FindResource("AccentDeepBrush")!;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        PrintService.OpenInBrowser(PrintService.BuildHtml(_projectName, _current, DateTime.Now), _current.Title);
    }
}
