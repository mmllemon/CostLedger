using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
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
/// 往来单位台账（跨项目）：选一个供应商/建设单位，一屏看清它在公司所有项目的
/// 合同额（合计 + 分项目）、已付/未付、已收/未收。
/// </summary>
public partial class PartyLedgerView : UserControl
{
    private readonly PartyLedgerService _svc = null!;
    private PartyLedger? _current;

    public PartyLedgerView()
    {
        InitializeComponent();
    }

    public PartyLedgerView(AppDbContext db) : this()
    {
        _svc = new PartyLedgerService(db);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var parties = await _svc.ListPartiesAsync();
        PartyCombo.ItemsSource = parties;
        PartyCombo.DisplayMemberBinding = new Binding("Name");
        PartyCombo.SelectedIndex = parties.Count > 0 ? 0 : -1;
    }

    private async void OnPartyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PartyCombo.SelectedItem is not Party p) return;
        _current = await _svc.BuildAsync(p.Id);
        Render(_current);
    }

    private void Render(PartyLedger l)
    {
        PartyTitle.Text = $"{l.PartyName}（{l.ProjectCount} 个项目 · {l.ContractCount} 份合同）";

        SummaryHost.Children.Clear();
        void Card(string label, decimal v, bool bad = false) => SummaryHost.Children.Add(MakeCard(label, Money(v), bad));
        Card("合同总额", l.ContractTotal);
        Card("对上合同额", l.UpstreamAmount);
        Card("对下合同额", l.DownstreamAmount);
        Card("累计付款", l.Paid);
        Card("未付", l.Unpaid, l.Unpaid > 0);
        Card("累计收款", l.Received);
        Card("未收", l.Unreceived, l.Unreceived > 0);

        FillTable(ProjectHost,
            new[] { "项目", "合同额", "累计付款", "未付", "累计收款" },
            l.ByProject.Select(r => (new[] { r.ProjectName, Money(r.ContractAmount), Money(r.Paid), Money(r.Unpaid), Money(r.Received) }, r.Unpaid > 0)).ToList(),
            breachColumn: 3);

        FillTable(ContractHost,
            new[] { "项目", "合同编号", "类型", "合同额", "已付", "未付" },
            l.Contracts.Select(r => (new[] { r.ProjectName, r.ContractCode, r.TypeCode, Money(r.Amount), Money(r.Paid), Money(r.Unpaid) }, r.Unpaid > 0)).ToList(),
            breachColumn: 5);
    }

    private Border MakeCard(string label, string value, bool bad)
    {
        var v = new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) };
        if (bad) v.Foreground = (SolidColorBrush)this.FindResource("RedBrush")!;
        return new Border
        {
            Classes = { "sidecard" },
            Width = 180,
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

    private void FillTable(Grid host, string[] headers, List<(string[] Cells, bool Breach)> rows, int breachColumn)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        for (int c = 0; c < headers.Length; c++)
            host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < rows.Count + 1; r++)
            host.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < headers.Length; c++)
            host.Children.Add(Cell(headers[c], 0, c, head: true, red: false));
        for (int r = 0; r < rows.Count; r++)
        {
            var (cells, breach) = rows[r];
            for (int c = 0; c < headers.Length; c++)
                host.Children.Add(Cell(cells[c], r + 1, c, head: false, red: breach && c == breachColumn));
        }
    }

    private TextBlock Cell(string text, int row, int col, bool head, bool red)
    {
        var tb = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 0, 24, 7) };
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

    private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var l = _current;
        var headers = new[] { "项目", "合同编号", "类型", "合同额", "已付", "未付" };
        var rows = l.Contracts
            .Select(r => new[] { r.ProjectName, r.ContractCode, r.TypeCode, Money(r.Amount), Money(r.Paid), Money(r.Unpaid) })
            .ToList();
        var footers = new List<string[]>
        {
            new[] { "合同总额", "", "", Money(l.ContractTotal), Money(l.Paid), Money(l.Unpaid) },
            new[] { "累计收款", "", "", "", Money(l.Received), "" },
        };
        var ledger = new LedgerResult($"往来单位台账 · {l.PartyName}", headers, rows, footers);
        PrintService.OpenInBrowser(PrintService.BuildHtml(l.PartyName, ledger, DateTime.Now), "往来单位台账");
    }
}
