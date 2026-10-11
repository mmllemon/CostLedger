using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
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
/// 项目经济驾驶舱：消费 EconomicFactsService.SummarizeProjectAsync 与 CostForecastService.GetSummaryAsync。
/// 收入 / 支出 / 已收 / 已付 / 资金压力 / 分配覆盖按栏分开，不跨栏相加；
/// 预计最终成本只来自冻结或草稿预测版本，无用版本时显示"尚无预测版本"，绝不用已知支出冒充。
/// </summary>
public class ProjectCockpitView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly EconomicFactsService _facts = null!;
    private readonly CostForecastService _forecast = null!;

    private UniformGrid _kpiHost = null!;
    private StackPanel _body = null!;
    private TextBlock _title = null!;
    private Project? _current;
    private ProjectEconomySummary? _economy;
    private CostForecastSummary? _summary;

    public ProjectCockpitView(AppDbContext db)
    {
        _db = db;
        _facts = new EconomicFactsService(db);
        _forecast = new CostForecastService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _title = new TextBlock { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        var top = Widgets.PageHeaderWithDynamicSubtitle("成本", "项目经济驾驶舱", "对上收入 · 对下支出 · 资金状态 · 成本预测 · 风险", _title,
            Widgets.ActionButton("刷新", () => _ = LoadAsync()),
            Widgets.ActionButton("打印项目经济一页", () => OnPrint(this, new RoutedEventArgs())));

        _kpiHost = Widgets.KpiGrid();
        _body = new StackPanel { Spacing = 14 };

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_kpiHost);
        root.Children.Add(_body);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task InitAsync()
    {
        // 项目由顶部全局切换器统一驱动，此处只加载默认第一个项目，避免出现第二个项目下拉。
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        if (_current is null && projects.Count > 0) { _current = projects[0]; await LoadAsync(); }
    }

    /// <summary>由顶部全局项目切换器驱动。</summary>
    public void SelectProject(Project project)
    {
        _current = project;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_current is null) return;
        _economy = await _facts.SummarizeProjectAsync(_current.Id);
        _summary = await _forecast.GetSummaryAsync(_current.Id);
        Render();
    }

    private void Render()
    {
        if (_current is null || _economy is null) return;
        var o = _economy;
        var f = _summary;

        _title.Text = $"{_current.Code} · {_current.Name}";

        _kpiHost.Children.Clear();
        void Kpi(string label, string value, bool bad = false, string? desc = null) => _kpiHost.Children.Add(Widgets.Kpi(label, value, bad, 0, desc));
        Kpi("项目预计收入", UiText.Money(o.ExpectedIncome), false, "对上收入板块已登记金额");
        Kpi("已知项目支出", UiText.Money(o.KnownExpenditure), false, "已登记外部金额事实 · 非财务实际成本");
        Kpi("已收（外部提供）", UiText.Money(o.Received), false, "来源：财务/项目部资金状态");
        Kpi("已付（外部提供）", UiText.Money(o.Paid), false, "来源：财务/项目部资金状态");
        Kpi("资金压力（已付−已收）", UiText.Money(o.FundingGap), o.FundingGap > 0, "已付 − 已收，供现金流关注");

        _body.Children.Clear();
        _body.Children.Add(BuildCostSummary(f));
        _body.Children.Add(BuildRiskAndQuality(o, f));
        if (f != null) _body.Children.Add(Widgets.Card("逐控制项预测（已知金额来自归属关系，不接受手填）", BuildForecastLines(f)));
        _body.Children.Add(Widgets.Card("业务板块分栏（不跨栏相加）", BuildSegments(o)));
        _body.Children.Add(Widgets.Card(null, BuildCaliberNote(o)));
    }

    /// <summary>最大风险项（按控制项偏差）+ 成本可解释程度，两栏并排。</summary>
    private Control BuildRiskAndQuality(ProjectEconomySummary o, CostForecastSummary? f)
    {
        // 左：最大风险项
        var riskBody = new StackPanel { Spacing = 8 };
        if (f is null || f.Lines.Count == 0)
        {
            riskBody.Children.Add(new TextBlock { Text = "尚无预测版本或无预测行，无法输出风险排名。", FontSize = 12, Foreground = Palette.Txt3 });
        }
        else
        {
            var overLines = f.Lines.Where(l => l.Variance > 0).OrderByDescending(l => l.Variance).Take(3).ToList();
            if (overLines.Count == 0)
                riskBody.Children.Add(new TextBlock { Text = "所有控制项均未超目标。", FontSize = 12, Foreground = Palette.Txt3 });
            else
            {
                var headers = new[] { "控制项", "目标", "预计最终", "差额" };
                var right = new HashSet<int> { 1, 2, 3 };
                var rows = overLines.Select(l => new[]
                {
                    l.Name, UiText.Money(l.TargetAmount), UiText.Money(l.EstimatedFinal), UiText.Money(l.Variance)
                });
                riskBody.Children.Add(Widgets.Table(headers, rows, right, _ => true));
            }
        }
        var riskHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        riskHeader.Children.Add(new TextBlock { Text = "最大风险项", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center });
        if (f is not null && f.VarianceTotal > 0) riskHeader.Children.Add(Widgets.Badge($"合计 {UiText.Money(f.VarianceTotal)}", "danger"));
        riskBody.Children.Insert(0, riskHeader);

        // 右：成本可解释程度
        var q = new StackPanel { Spacing = 2 };
        q.Children.Add(new TextBlock { Text = "成本可解释程度", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Palette.Ink, Margin = new Thickness(0, 0, 0, 4) });
        q.Children.Add(Widgets.Field("已分配归属", UiText.Money(o.AllocatedAmount)));
        q.Children.Add(Widgets.Track((double)o.CoverageRate));
        q.Children.Add(Widgets.Field("待归属金额", UiText.Money(o.UnallocatedAmount) + " · " + ((1 - o.CoverageRate) * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%", o.UnallocatedAmount > 0));
        q.Children.Add(Widgets.Field("暂估分配", UiText.Money(o.EstimatedAllocationAmount)));
        q.Children.Add(Widgets.Field("待确认状态", UiText.Money(o.PendingConfirmationAmount)));
        if (f is not null)
            q.Children.Add(Widgets.Field("未纳入预测事项", UiText.Money(f.UnincludedChangeAmount), f.UnincludedChangeAmount > 0));
        q.Children.Add(Widgets.Hint("不是录入越多越好：待归属与未纳入预测的金额会被如实标出，不会伪装成已确认成本。"));

        return Widgets.TwoCol(Widgets.Card(null, riskBody), Widgets.Card(null, q));
    }

    private Control BuildCostSummary(CostForecastSummary? f)
    {
        var body = new StackPanel { Spacing = 10 };
        if (f is null)
        {
            var hero = new TextBlock { Text = "尚无预测版本", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Palette.Txt3 };
            body.Children.Add(new TextBlock { Text = "当前预计最终成本", FontSize = 11, Foreground = Palette.Txt3, LetterSpacing = 1.2 });
            body.Children.Add(hero);
            body.Children.Add(Widgets.Hint(
                "没有冻结或草稿预测版本时，系统不给出预计最终成本，也不会用已知支出冒充它。" +
                "请到「成本预测版本」建立版本并冻结。"));
        }
        else
        {
            var over = f.VarianceTotal;
            body.Children.Add(new TextBlock { Text = $"当前预计最终成本（预测 v{f.VersionNumber} · {f.ForecastDate:yyyy-MM-dd} · {UiText.VersionStatus(f.Status)}）", FontSize = 11, Foreground = Palette.Txt3, LetterSpacing = 1.2 });

            var heroText = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            heroText.Children.Add(new TextBlock
            {
                Text = UiText.Money(f.EstimatedFinalTotal),
                Classes = { "hero-number" },
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Consolas, sans-serif")
            });
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            sub.Children.Add(new TextBlock { Text = $"目标成本 {UiText.Money(f.TargetTotal)}", Foreground = Palette.Txt2, FontSize = 12.5 });
            sub.Children.Add(new TextBlock { Text = $"预计超目标 {UiText.Money(over)}", Foreground = over > 0 ? Palette.Red : Palette.Txt2, FontSize = 12.5, FontWeight = FontWeight.Bold });
            sub.Children.Add(new TextBlock { Text = $"可信度 {UiText.Confidence(f.Confidence)}", Foreground = Palette.AccentDeep, FontSize = 12.5 });
            sub.Children.Add(new TextBlock { Text = $"覆盖 {f.ForecastLineCount}/{f.ActiveControlItemCount} 个控制项", Foreground = Palette.Txt2, FontSize = 12.5 });
            heroText.Children.Add(sub);

            var heroGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(heroText, 0);
            var ring = Widgets.Ring((double)f.CoverageRate, (f.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%", 100);
            Grid.SetColumn(ring, 1);
            heroGrid.Children.Add(heroText);
            heroGrid.Children.Add(ring);
            body.Children.Add(heroGrid);

            var badges = new WrapPanel();
            if (f.UnallocatedAmount > 0) badges.Children.Add(Widgets.Badge($"未归属 {UiText.Money(f.UnallocatedAmount)}", "danger"));
            if (f.UnincludedChangeAmount > 0) badges.Children.Add(Widgets.Badge($"未纳入变化 {UiText.Money(f.UnincludedChangeAmount)}", "accent"));
            if (f.PendingConfirmationChangeAmount > 0) badges.Children.Add(Widgets.Badge($"依赖未确认 {UiText.Money(f.PendingConfirmationChangeAmount)}", "accent"));
            if (over > 0) badges.Children.Add(Widgets.Badge($"预计超目标 {UiText.Money(over)}", "danger"));
            else badges.Children.Add(Widgets.Badge($"预计节余 {UiText.Money(-over)}", "ok"));
            if (badges.Children.Count > 0) body.Children.Add(badges);
            body.Children.Add(Widgets.Hint($"编制假设：{f.Basis ?? "（未填写，冻结时必须填写）"}"));

            var cards = new WrapPanel();
            cards.Children.Add(Widgets.Kpi("已知金额（自动取）", UiText.Money(f.KnownTotal)));
            cards.Children.Add(Widgets.Kpi("剩余估计", UiText.Money(f.RemainingTotal)));
            cards.Children.Add(Widgets.Kpi("风险调整", UiText.Money(f.RiskTotal)));
            cards.Children.Add(Widgets.Kpi("未归属金额", UiText.Money(f.UnallocatedAmount), f.UnallocatedAmount > 0));
            cards.Children.Add(Widgets.Kpi("未纳入预测的变化", UiText.Money(f.UnincludedChangeAmount), f.UnincludedChangeAmount > 0));
            cards.Children.Add(Widgets.Kpi("依赖未确认事项", UiText.Money(f.PendingConfirmationChangeAmount), f.PendingConfirmationChangeAmount > 0));
            cards.Children.Add(Widgets.Kpi("覆盖率", (f.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%"));
            if (f.PreviousVersionId is int pv)
                cards.Children.Add(Widgets.Kpi($"较上一冻结版本(v{pv})", UiText.Money(f.ChangeSincePrevious), f.ChangeSincePrevious > 0));
            body.Children.Add(cards);
        }
        return Widgets.Card(null, body);
    }

    private static Control BuildForecastLines(CostForecastSummary f)
    {
        var headers = new[] { "控制项", "目标", "已知(自动)", "剩余估计", "风险调整", "预计最终", "偏差", "精度封顶" };
        var right = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var rows = f.Lines.Select(l => new[]
        {
            l.Name, UiText.Money(l.TargetAmount), UiText.Money(l.KnownAmount),
            UiText.Money(l.RemainingEstimate), UiText.Money(l.RiskAdjustment),
            UiText.Money(l.EstimatedFinal), UiText.Money(l.Variance), UiText.Precision(l.PrecisionCap)
        });
        return Widgets.Table(headers, rows, right, r => Parse(r[6]) > 0);
    }

    private static Control BuildSegments(ProjectEconomySummary o)
    {
        var headers = new[] { "业务板块", "金额", "记录数", "最近截至日期", "是否计入可分配分母" };
        var right = new HashSet<int> { 1, 2 };
        var rows = o.Segments.Select(s => new[]
        {
            UiText.Segment(s.Segment), UiText.Money(s.Amount), s.FactCount.ToString(),
            UiText.Date(s.LatestAsOfDate), AllocatableSegments.Values.Contains(s.Segment) ? "是" : "否"
        });
        return Widgets.Table(headers, rows, right);
    }

    private static Control BuildCaliberNote(ProjectEconomySummary o) => new TextBlock
    {
        Text = $"数据截至：{UiText.Date(o.LatestAsOfDate)}　编制：成本岗。" +
               "已收 / 已付为外部提供的资金状态，不是财务确认；已知支出为成本岗登记的外部金额事实。" +
               "预计最终成本来自成本人员预测版本，非财务结论。",
        FontSize = 11,
        Foreground = Palette.Txt2,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19
    };

    private static decimal Parse(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) ? v : 0m;

    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        if (_current is null || _economy is null) return;
        var o = _economy;
        var f = _summary;
        var headers = new[] { "指标", "数值" };
        var rows = new List<string[]>
        {
            new[] { "预计收入", UiText.Money(o.ExpectedIncome) },
            new[] { "已知项目支出", UiText.Money(o.KnownExpenditure) },
            new[] { "已收", UiText.Money(o.Received) },
            new[] { "已付", UiText.Money(o.Paid) },
            new[] { "资金压力（已付−已收）", UiText.Money(o.FundingGap) },
            new[] { "目标成本", f is null ? "尚无预测版本" : UiText.Money(f.TargetTotal) },
            new[] { "预计最终成本", f is null ? "尚无预测版本" : UiText.Money(f.EstimatedFinalTotal) },
            new[] { "预计超目标", f is null ? "尚无预测版本" : UiText.Money(f.VarianceTotal) },
            new[] { "未归属金额", UiText.Money(o.UnallocatedAmount) },
            new[] { "覆盖率", (o.CoverageRate * 100m).ToString("0.0", CultureInfo.CurrentCulture) + "%" },
        };
        var ledger = new LedgerResult("项目经济一页", headers, rows, Array.Empty<string[]>());
        var company = await new SystemParamService(_db).GetAsync("公司名称");
        var caliber = _current.Name + "，数据截至 " + UiText.Date(o.LatestAsOfDate) +
            "。本表为项目成本管理信息，已收/已付来自外部汇总，预计最终成本来自成本人员预测版本，非财务结论。";
        PrintService.OpenInBrowser(PrintService.BuildHtml(_current.Name, ledger, DateTime.Now, company, caliber), "项目经济一页");
    }
}
