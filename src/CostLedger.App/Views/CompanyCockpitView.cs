using Avalonia;
using Avalonia.Controls;
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
/// 公司经营驾驶舱：全部在管项目的同一口径汇总。
/// 合计 = 各项目逐列相加，不重新计算；没有预测版本的项目"预计最终"显示为 —，不计入合计。
/// </summary>
public class CompanyCockpitView : UserControl
{
    private readonly CompanyService _svc = null!;
    private readonly WrapPanel _kpiHost = new();
    private readonly StackPanel _body = new();
    private readonly StackPanel _heroHost = new();
    private CompanySummary? _summary;

    public CompanyCockpitView(AppDbContext db)
    {
        _svc = new CompanyService(db);
        Content = BuildUi();
        _ = LoadAsync();
    }

    private Control BuildUi()
    {
        var title = new TextBlock { Text = "公司经营驾驶舱", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center };
        var refresh = new Button { Content = "刷新" };
        refresh.Classes.Add("pill");
        refresh.Click += async (_, _) => await LoadAsync();
        var print = new Button { Content = "打印公司汇总" };
        print.Classes.Add("pill");
        print.Click += OnPrint;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(title);
        top.Children.Add(refresh);
        top.Children.Add(print);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        root.Children.Add(top);
        root.Children.Add(Widgets.Card(null, _heroHost));
        root.Children.Add(Widgets.Card(null, _kpiHost));
        root.Children.Add(_body);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task LoadAsync()
    {
        _summary = await _svc.SummarizeAsync();
        Render();
    }

    private void RenderHero(CompanySummary s)
    {
        var left = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = $"公司在管项目 {s.ProjectCount} 个 · 汇总口径截至 {UiText.Date(s.Projects.Select(p => p.AsOfDate).Where(d => d.HasValue).DefaultIfEmpty(null).Max())}",
            FontSize = 11, Foreground = Palette.Txt3, LetterSpacing = 1.0
        });
        left.Children.Add(new TextBlock
        {
            Text = s.ProjectCount == s.MissingForecastCount ? "暂无预测版本" : UiText.Money(s.EstimatedFinal),
            FontSize = 44, FontWeight = FontWeight.Bold, Foreground = Palette.Ink
        });
        left.Children.Add(new TextBlock
        {
            Text = $"公司预计最终成本　目标 {UiText.Money(s.TargetTotal)}",
            FontSize = 12.5, Foreground = Palette.Txt2
        });

        var badges = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        int overCount = s.Projects.Count(p => p.Variance.HasValue && p.Variance.Value > 0);
        int underCount = s.Projects.Count(p => p.Variance.HasValue && p.Variance.Value < 0);
        if (overCount > 0) badges.Children.Add(Widgets.Badge($"{overCount} 个项目预计超目标", "danger"));
        if (underCount > 0) badges.Children.Add(Widgets.Badge($"{underCount} 个项目预计节余", "accent"));
        if (s.MissingForecastCount > 0) badges.Children.Add(Widgets.Badge($"{s.MissingForecastCount} 个项目尚无预测版本", "muted"));
        if (s.UnallocatedAmount > 0) badges.Children.Add(Widgets.Badge($"待归属 {UiText.Money(s.UnallocatedAmount)}", "accent"));
        if (badges.Children.Count > 0) left.Children.Add(badges);

        var ring = Widgets.Ring((double)s.CoverageRate, (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%", 108);

        var heroGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(ring, 1);
        heroGrid.Children.Add(left);
        heroGrid.Children.Add(ring);
        _heroHost.Children.Add(heroGrid);
    }

    private void Render()
    {
        var s = _summary;
        if (s is null) return;

        _kpiHost.Children.Clear();
        _body.Children.Clear();
        _heroHost.Children.Clear();
        RenderHero(s);

        void Kpi(string l, string v, bool bad = false) => _kpiHost.Children.Add(Widgets.Kpi(l, v, bad));
        Kpi("在管项目", s.ProjectCount.ToString());
        Kpi("公司预计收入", UiText.Money(s.ExpectedIncome));
        Kpi("公司已知支出", UiText.Money(s.KnownExpenditure));
        Kpi("公司预计最终成本", s.ProjectCount == s.MissingForecastCount ? "暂无预测版本" : UiText.Money(s.EstimatedFinal));
        Kpi("公司预计超/节目标", UiText.Money(s.Variance), s.Variance > 0);
        Kpi("公司资金压力（已付−已收）", UiText.Money(s.FundingGap), s.FundingGap > 0);
        Kpi("公司未归属金额", UiText.Money(s.UnallocatedAmount), s.UnallocatedAmount > 0);
        Kpi("公司覆盖率", (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%");

        _body.Children.Add(Widgets.Card("按项目经营汇总（每行 = 该项目页同一口径；合计为逐列相加）", BuildProjectTable(s)));
        _body.Children.Add(Widgets.Card(null, new TextBlock
        {
            Text = $"数据截至：{UiText.Date(s.Projects.Select(p => p.AsOfDate).Where(d => d.HasValue).DefaultIfEmpty(null).Max())}　编制：成本岗。" +
                   (s.MissingForecastCount > 0
                       ? $"其中 {s.MissingForecastCount} 个项目没有预测版本，其「预计最终」为 —，不计入公司合计。"
                       : "") +
                   "公司合计由项目口径逐列相加得到，不下钻、不重算；本表为内部管理信息，非财务结论。",
            FontSize = 11, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap, LineHeight = 1.7
        }));
    }

    private static Control BuildProjectTable(CompanySummary s)
    {
        var headers = new[] { "项目", "预计收入", "已知支出", "预计最终", "目标", "预计超/节", "资金压力", "覆盖率", "截至" };
        var right = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7 };
        var rows = s.Projects.Select(p => new[]
        {
            $"{p.Code} {p.Name}",
            UiText.Money(p.ExpectedIncome),
            UiText.Money(p.KnownExpenditure),
            p.EstimatedFinal.HasValue ? UiText.Money(p.EstimatedFinal.Value) : "—",
            p.TargetTotal.HasValue ? UiText.Money(p.TargetTotal.Value) : "—",
            p.Variance.HasValue ? UiText.Money(p.Variance.Value) : "—",
            UiText.Money(p.FundingGap),
            (p.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%",
            UiText.Date(p.AsOfDate)
        }).ToList();

        var footer = new[]
        {
            "合计",
            UiText.Money(s.ExpectedIncome),
            UiText.Money(s.KnownExpenditure),
            s.ProjectCount == s.MissingForecastCount ? "—" : UiText.Money(s.EstimatedFinal),
            UiText.Money(s.TargetTotal),
            UiText.Money(s.Variance),
            UiText.Money(s.FundingGap),
            (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%",
            ""
        };

        var grid = Widgets.TableWithFooter(headers, rows, footer, right,
            r => decimal.TryParse(r[5], NumberStyles.Any, CultureInfo.CurrentCulture, out var v) && v > 0);
        return grid;
    }

    private async void OnPrint(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_summary is null) return;
        var s = _summary;
        var headers = new[] { "项目", "预计收入", "已知支出", "预计最终", "目标", "预计超/节", "资金压力", "覆盖率" };
        var rows = s.Projects.Select(p => new[]
        {
            $"{p.Code} {p.Name}",
            UiText.Money(p.ExpectedIncome), UiText.Money(p.KnownExpenditure),
            p.EstimatedFinal.HasValue ? UiText.Money(p.EstimatedFinal.Value) : "—",
            p.TargetTotal.HasValue ? UiText.Money(p.TargetTotal.Value) : "—",
            p.Variance.HasValue ? UiText.Money(p.Variance.Value) : "—",
            UiText.Money(p.FundingGap),
            (p.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%"
        }).ToList();
        var footers = new List<string[]>
        {
            new[] { "合计", UiText.Money(s.ExpectedIncome), UiText.Money(s.KnownExpenditure),
                    s.ProjectCount == s.MissingForecastCount ? "—" : UiText.Money(s.EstimatedFinal),
                    UiText.Money(s.TargetTotal), UiText.Money(s.Variance), UiText.Money(s.FundingGap),
                    (s.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%" }
        };
        var ledger = new LedgerResult("公司在管项目经营汇总", headers, rows, footers);
        var caliber = "数据截至 " + UiText.Date(s.Projects.Select(p => p.AsOfDate).Where(d => d.HasValue).DefaultIfEmpty(null).Max()) +
            "。合计由项目口径逐列相加，不下钻不重算；没有预测版本的项目预计最终为—；本表为内部管理信息，非财务结论。";
        PrintService.OpenInBrowser(PrintService.BuildHtml("公司在管项目经营汇总", ledger, DateTime.Now, null, caliber), "公司汇总");
        await Task.CompletedTask;
    }
}
