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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// M3-B 对账中心：三算对比（目标/实际/结算）· 合同 vs 成本 · 按科目 目标 vs 实际。
/// 差异为红色（超支/超合同额），与设计系统语义一致。
/// </summary>
public partial class ReconciliationView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly ReconciliationService _recon = null!;
    private readonly PaymentService _pay = null!;

    public ReconciliationView()
    {
        InitializeComponent();
    }

    public ReconciliationView(AppDbContext db) : this()
    {
        _db = db;
        _recon = new ReconciliationService(db, new CostSpineService(db));
        _pay = new PaymentService(db);
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
        await LoadAsync(p.Id);
    }

    private async Task LoadAsync(int projectId)
    {
        var summary = await _recon.ThreeWaySummaryAsync(projectId);
        Fill(ThreeWayHost,
            new[] { "目标成本", "已发生", "竣工结算", "实际-目标", "结算-目标" },
            new List<(string[] Cells, bool Breach)>
            {
                (new[]
                {
                    Money(summary.Target), Money(summary.Incurred), Money(summary.Settled),
                    Money(summary.TargetVsIncurred), Money(summary.TargetVsSettled)
                }, summary.TargetVsIncurred > 0)
            },
            breachColumn: 3);
        TargetCapital.Text = "目标成本大写：" + MoneyUtil.ToChineseCapital(summary.Target);

        // 对上 vs 对下
        var updown = await _recon.UpDownVarianceAsync(projectId);
        var upRows = updown.Rows
            .Select(r => (new[] { r.TypeCode, r.IsUpstream ? "对上" : "对下", Money(r.Amount) }, false))
            .ToList();
        upRows.Add((new[] { "—", "对上合计", Money(updown.Upstream) }, false));
        upRows.Add((new[] { "—", "对下合计", Money(updown.Downstream) }, false));
        upRows.Add((new[] { "—", "差额", Money(updown.Diff) }, updown.Diff < 0));
        Fill(UpDownHost, new[] { "合同类型", "方向", "金额" }, upRows, breachColumn: 2);

        // 资金 vs 应付应收
        var fund = await _recon.FundReconAsync(projectId);
        Fill(FundHost,
            new[] { "类别", "应", "实", "缺口" },
            fund.Select(r => (new[] { r.Kind, Money(r.Should), Money(r.Actual), Money(r.Gap) }, r.Gap > 0)).ToList(),
            breachColumn: 3);

        // 收款台账（对甲方）
        await FillFundLedgerAsync(ReceiveHost, projectId, PaymentDirection.Receive);
        // 付款台账（对下游）
        await FillFundLedgerAsync(PayHost, projectId, PaymentDirection.Pay);

        var contractRows = await _recon.ContractVsCostAsync(projectId);
        Fill(ContractHost,
            new[] { "合同编号", "名称", "合同额", "已发生", "差异" },
            contractRows.Select(r => (
                new[] { r.ContractCode, r.Title, Money(r.ContractAmount), Money(r.Incurred), Money(r.Diff) },
                r.IsBreach)).ToList(),
            breachColumn: 4);

        var catRows = await _recon.CategoryTargetActualAsync(projectId);
        Fill(CategoryHost,
            new[] { "成本科目", "目标", "实际", "偏差" },
            catRows.Select(r => (
                new[] { r.Category, Money(r.Target), Money(r.Incurred), Money(r.Variance) },
                r.IsOver)).ToList(),
            breachColumn: 3);
    }

    private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>收款/付款台账：合同额 / 累计 / 比例 / 未收(付)。</summary>
    private async Task FillFundLedgerAsync(Grid host, int projectId, PaymentDirection direction)
    {
        var rows = await _pay.FundLedgerAsync(projectId, direction);
        Fill(host,
            new[] { "合同编号", "往来单位", "合同额", "累计", "比例", "未收(付)" },
            rows.Select(r => (new[]
            {
                r.ContractCode, r.PartyName, Money(r.ContractAmount), Money(r.Cumulative),
                (r.Ratio * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%", Money(r.Outstanding)
            }, false)).ToList(),
            breachColumn: -1);
    }

    /// <summary>把表头 + 行数据渲染进一个 Grid；Breach 行在 breachColumn 列以红色显示。</summary>
    private void Fill(Grid host, string[] headers, List<(string[] Cells, bool Breach)> rows, int breachColumn)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();

        for (int c = 0; c < headers.Length; c++)
            host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < rows.Count + 1; r++)
            host.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < headers.Length; c++)
            host.Children.Add(Cell(headers[c], 0, c, header: true, red: false));

        for (int r = 0; r < rows.Count; r++)
        {
            var (cells, breach) = rows[r];
            for (int c = 0; c < headers.Length; c++)
                host.Children.Add(Cell(cells[c], r + 1, c, header: false, red: breach && c == breachColumn));
        }
    }

    private TextBlock Cell(string text, int row, int col, bool header, bool red)
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
            tb.Foreground = Palette.Find("Txt3Brush");
        }
        else if (red)
        {
            tb.Foreground = Palette.Red;
            tb.FontWeight = FontWeight.SemiBold;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }
}
