using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
/// 内部汇报报表：在三个模板间切换（项目经济一页 / 往来单位敞口 / 公司项目汇总），
/// 各带口径声明与数据截至，一键打印 A4。回答"报表是否写死"——不写死，模板可选、可打印。
/// </summary>
public class InternalReportView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly EconomicFactsService _facts = null!;
    private readonly CostForecastService _forecast = null!;
    private readonly CompanyService _company = null!;
    private readonly PartyPanoramaService _parties = null!;

    private readonly string[] _templates = { "项目经济一页", "往来单位敞口（跨项目）", "公司项目汇总" };
    private int _templateIndex;
    private readonly StackPanel _chipHost = new();
    private readonly StackPanel _preview = new();
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap };

    private ComboBox _projCombo = null!;
    private ComboBox _partyCombo = null!;
    private List<Project> _projects = new();
    private List<Party> _partyList = new();

    public InternalReportView(AppDbContext db)
    {
        _db = db;
        _facts = new EconomicFactsService(db);
        _forecast = new CostForecastService(db);
        _company = new CompanyService(db);
        _parties = new PartyPanoramaService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _projCombo = new ComboBox { Width = 260, PlaceholderText = "选择项目" };
        _projCombo.DisplayMemberBinding = new Binding("Name");
        _projCombo.SelectionChanged += async (_, _) => await RenderPreviewAsync();
        _partyCombo = new ComboBox { Width = 260, PlaceholderText = "选择单位" };
        _partyCombo.DisplayMemberBinding = new Binding("Name");
        _partyCombo.SelectionChanged += async (_, _) => await RenderPreviewAsync();

        var print = new Button { Content = "打印 / 另存 PDF" };
        print.Classes.Add("pill");
        print.Click += OnPrint;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(new TextBlock { Text = "内部汇报报表（A4）", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(print);

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (int i = 0; i < _templates.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = _templates[i] };
            b.Classes.Add("pill");
            b.Click += async (_, _) => { _templateIndex = idx; await RenderPreviewAsync(); };
            chipRow.Children.Add(b);
        }

        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        selectors.Children.Add(new TextBlock { Text = "项目", VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Txt2 });
        selectors.Children.Add(_projCombo);
        selectors.Children.Add(new TextBlock { Text = "单位", VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Txt2 });
        selectors.Children.Add(_partyCombo);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        root.Children.Add(top);
        root.Children.Add(Widgets.Card("选择模板", chipRow));
        root.Children.Add(Widgets.Card("选择数据范围", selectors));
        root.Children.Add(_status);
        root.Children.Add(_preview);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task InitAsync()
    {
        _projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        _partyList = await _parties.ListPartiesAsync();
        _projCombo.ItemsSource = _projects;
        _partyCombo.ItemsSource = _partyList;
        if (_projects.Count > 0) _projCombo.SelectedIndex = 0;
        if (_partyList.Count > 0) _partyCombo.SelectedIndex = 0;
    }

    private async Task RenderPreviewAsync()
    {
        _preview.Children.Clear();
        if (_templateIndex == 0) await RenderProjectAsync();
        else if (_templateIndex == 1) await RenderPartyAsync();
        else await RenderCompanyAsync();
    }

    private async Task RenderProjectAsync()
    {
        if (_projCombo.SelectedItem is not Project p) { _status.Text = "请选择项目。"; return; }
        var e = await _facts.SummarizeProjectAsync(p.Id);
        var f = await _forecast.GetSummaryAsync(p.Id);
        var headers = new[] { "指标", "数值" };
        var rows = new List<string[]>
        {
            new[] { "预计收入", UiText.Money(e.ExpectedIncome) },
            new[] { "已知项目支出", UiText.Money(e.KnownExpenditure) },
            new[] { "已收", UiText.Money(e.Received) },
            new[] { "已付", UiText.Money(e.Paid) },
            new[] { "资金压力（已付−已收）", UiText.Money(e.FundingGap) },
            new[] { "目标成本", f is null ? "尚无预测版本" : UiText.Money(f.TargetTotal) },
            new[] { "预计最终成本", f is null ? "尚无预测版本" : UiText.Money(f.EstimatedFinalTotal) },
            new[] { "预计超目标", f is null ? "尚无预测版本" : UiText.Money(f.VarianceTotal) },
            new[] { "未归属金额", UiText.Money(e.UnallocatedAmount) },
            new[] { "覆盖率", (e.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%" },
        };
        _status.Text = $"模板：项目经济一页 · {p.Name} · 数据截至 {UiText.Date(e.LatestAsOfDate)}";
        _preview.Children.Add(A4Preview(p.Name, "项目经济汇报表", "打印日期 " + DateTime.Now.ToString("yyyy-MM-dd"), headers, rows, null,
            $"{p.Name}，数据截至 {UiText.Date(e.LatestAsOfDate)}。已收/已付为外部提供，预计最终成本来自成本人员预测版本，非财务结论。"));
    }

    private async Task RenderPartyAsync()
    {
        if (_partyCombo.SelectedItem is not Party p) { _status.Text = "请选择往来单位。"; return; }
        var l = await _parties.BuildAsync(p.Id);
        var headers = new[] { "项目", "角色（板块）", "合同额", "累计付款", "未付（推算）", "累计收款", "未收（推算）" };
        var rows = l.ByProject.Select(r => new[]
        {
            r.ProjectName, r.Roles, UiText.Money(r.ContractAmount), UiText.Money(r.Paid),
            UiText.Money(r.Unpaid), UiText.Money(r.Received), UiText.Money(r.Unreceived)
        }).ToList();
        var footers = new List<string[]>
        {
            new[] { "合计", "不跨板块合并", UiText.Money(l.ContractTotal), UiText.Money(l.Paid),
                    UiText.Money(l.Unpaid), UiText.Money(l.Received), UiText.Money(l.Unreceived) }
        };
        _status.Text = $"模板：往来单位敞口 · {l.PartyName} · 数据截至 {UiText.Date(l.AsOfDate)}";
        _preview.Children.Add(A4Preview(l.PartyName, "往来单位资金敞口汇报表", "打印日期 " + DateTime.Now.ToString("yyyy-MM-dd"), headers, rows, footers,
            PartyLedgerCaliber.Build(UiText.Date(l.AsOfDate))));
    }

    private async Task RenderCompanyAsync()
    {
        var s = await _company.SummarizeAsync();
        var headers = new[] { "项目", "预计收入", "已知支出", "预计最终", "目标", "预计超/节", "资金压力", "覆盖率" };
        var rows = s.Projects.Select(p => new[]
        {
            $"{p.Code} {p.Name}", UiText.Money(p.ExpectedIncome), UiText.Money(p.KnownExpenditure),
            p.EstimatedFinal.HasValue ? UiText.Money(p.EstimatedFinal.Value) : "—",
            p.TargetTotal.HasValue ? UiText.Money(p.TargetTotal.Value) : "—",
            p.Variance.HasValue ? UiText.Money(p.Variance.Value) : "—",
            UiText.Money(p.FundingGap), (p.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%"
        }).ToList();
        var footers = new List<string[]>
        {
            new[] { "合计", UiText.Money(s.ExpectedIncome), UiText.Money(s.KnownExpenditure),
                    s.ProjectCount == s.MissingForecastCount ? "—" : UiText.Money(s.EstimatedFinal),
                    UiText.Money(s.TargetTotal), UiText.Money(s.Variance), UiText.Money(s.FundingGap),
                    (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%" }
        };
        _status.Text = $"模板：公司项目汇总 · 在管 {s.ProjectCount} 个项目";
        _preview.Children.Add(A4Preview("公司在管项目经营汇总", "公司在管项目经营汇总表", "打印日期 " + DateTime.Now.ToString("yyyy-MM-dd"), headers, rows, footers,
            $"合计由项目口径逐列相加，不下钻不重算；没有预测版本的项目预计最终为—；本表为内部管理信息，非财务结论。"));
    }

    /// <summary>屏幕上的 A4 纸样预览（与打印用同一套表头/口径/签章结构）。</summary>
    private static Control A4Preview(string company, string title, string meta, string[] headers, List<string[]> rows,
        List<string[]>? footers, string caliber)
    {
        var paper = new StackPanel { Spacing = 12, Width = 720, HorizontalAlignment = HorizontalAlignment.Left };
        paper.Children.Add(new TextBlock { Text = "某建设集团", FontSize = 13, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        paper.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, LetterSpacing = 2 });
        paper.Children.Add(new TextBlock { Text = meta, FontSize = 11.5, Foreground = Palette.Txt2, HorizontalAlignment = HorizontalAlignment.Right });
        paper.Children.Add(Widgets.Table(headers, rows));

        var caliberBlock = new Border
        {
            BorderBrush = Palette.Txt3,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 4, 0, 4),
            Child = new TextBlock { Text = caliber, FontSize = 11, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap, LineHeight = 1.7 }
        };
        paper.Children.Add(caliberBlock);
        paper.Children.Add(new TextBlock { Text = "编制：成本岗　　审核：　　　　　日期：", FontSize = 12, Foreground = Palette.Txt2, Margin = new Thickness(0, 8, 0, 0) });

        return new Border
        {
            Background = Palette.White,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(28),
            Child = paper
        };
    }

    private async void OnPrint(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var company = await new SystemParamService(_db).GetAsync("公司名称");
        if (_templateIndex == 0)
        {
            if (_projCombo.SelectedItem is not Project p) return;
            var ec = await _facts.SummarizeProjectAsync(p.Id);
            var f = await _forecast.GetSummaryAsync(p.Id);
            var rows = new List<string[]>
            {
                new[] { "预计收入", UiText.Money(ec.ExpectedIncome) },
                new[] { "已知项目支出", UiText.Money(ec.KnownExpenditure) },
                new[] { "已收", UiText.Money(ec.Received) },
                new[] { "已付", UiText.Money(ec.Paid) },
                new[] { "预计最终成本", f is null ? "尚无预测版本" : UiText.Money(f.EstimatedFinalTotal) },
            };
            var ledger = new LedgerResult("项目经济汇报表", new[] { "指标", "数值" }, rows, Array.Empty<string[]>());
            PrintService.OpenInBrowser(PrintService.BuildHtml(p.Name, ledger, DateTime.Now, company,
                $"{p.Name}，数据截至 {UiText.Date(ec.LatestAsOfDate)}。本表为内部管理信息，非财务结论。"), "项目经济一页");
        }
        else if (_templateIndex == 1)
        {
            if (_partyCombo.SelectedItem is not Party p) return;
            var l = await _parties.BuildAsync(p.Id);
            var headers = new[] { "项目", "角色（板块）", "合同额", "累计付款", "未付（推算）", "累计收款", "未收（推算）" };
            var rows = l.ByProject.Select(r => new[]
            {
                r.ProjectName, r.Roles, UiText.Money(r.ContractAmount), UiText.Money(r.Paid),
                UiText.Money(r.Unpaid), UiText.Money(r.Received), UiText.Money(r.Unreceived)
            }).ToList();
            var footers = new List<string[]>
            {
                new[] { "合计", "不跨板块合并", UiText.Money(l.ContractTotal), UiText.Money(l.Paid),
                        UiText.Money(l.Unpaid), UiText.Money(l.Received), UiText.Money(l.Unreceived) }
            };
            var ledger = new LedgerResult("往来单位资金敞口汇报表", headers, rows, footers);
            PrintService.OpenInBrowser(PrintService.BuildHtml(l.PartyName, ledger, DateTime.Now, company, PartyLedgerCaliber.Build(UiText.Date(l.AsOfDate))), "往来单位台账");
        }
        else
        {
            var s = await _company.SummarizeAsync();
            var headers = new[] { "项目", "预计收入", "已知支出", "预计最终", "目标", "预计超/节", "资金压力", "覆盖率" };
            var rows = s.Projects.Select(p => new[]
            {
                $"{p.Code} {p.Name}", UiText.Money(p.ExpectedIncome), UiText.Money(p.KnownExpenditure),
                p.EstimatedFinal.HasValue ? UiText.Money(p.EstimatedFinal.Value) : "—",
                p.TargetTotal.HasValue ? UiText.Money(p.TargetTotal.Value) : "—",
                p.Variance.HasValue ? UiText.Money(p.Variance.Value) : "—",
                UiText.Money(p.FundingGap), (p.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%"
            }).ToList();
            var footers = new List<string[]>
            {
                new[] { "合计", UiText.Money(s.ExpectedIncome), UiText.Money(s.KnownExpenditure),
                        s.ProjectCount == s.MissingForecastCount ? "—" : UiText.Money(s.EstimatedFinal),
                        UiText.Money(s.TargetTotal), UiText.Money(s.Variance), UiText.Money(s.FundingGap),
                        (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%" }
            };
            var ledger = new LedgerResult("公司在管项目经营汇总表", headers, rows, footers);
            PrintService.OpenInBrowser(PrintService.BuildHtml("公司在管项目经营汇总", ledger, DateTime.Now, company,
                "合计由项目口径逐列相加，不下钻不重算；本表为内部管理信息，非财务结论。"), "公司汇总");
        }
    }
}
