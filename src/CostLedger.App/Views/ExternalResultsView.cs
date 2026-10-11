using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// 结算结果分析（P4）：登记外部成果结果（进度款报量/收方/送审/审定）的报送与确认金额，
/// 汇总差额用于成本分析。**不生成成果文件、不保存计价依据或计算底稿**。
/// </summary>
public class ExternalResultsView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly ExternalResultService _svc = null!;
    private Project? _current;
    private List<CostControlItem> _items = new();

    private readonly TextBlock _title = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
    private readonly UniformGrid _kpis = Widgets.KpiGrid();
    private readonly StackPanel _host = new();
    private readonly StackPanel _varianceHost = new();

    public ExternalResultsView(AppDbContext db)
    {
        _db = db;
        _svc = new ExternalResultService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        var top = Widgets.PageHeaderWithDynamicSubtitle("结算", "结算结果分析",
            "登记外部成果结果；不生成报量表/签证单/收方单/送审结算书", _title,
            Widgets.PrimaryActionButton("＋ 登记成果结果", () => OpenRegister()));

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card(null, _kpis));
        root.Children.Add(Widgets.Card("结算对目标/预测的差异（三口径并列，不加总）", _varianceHost));
        root.Children.Add(Widgets.Card("成果结果明细（报送/确认两金额独立；差额 = 确认 − 报送）", _host));
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    public void SelectProject(Project project) { _current = project; _ = ReloadAsync(); }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        if (_current is null && projects.Count > 0) { _current = projects[0]; await ReloadAsync(); }
    }

    private async Task ReloadAsync()
    {
        if (_current is null) return;
        _title.Text = $"{_current.Code} · {_current.Name}";
        _items = await _db.CostControlItems.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync();

        var s = await _svc.SummarizeAsync(_current.Id);
        _kpis.Children.Clear();
        _kpis.Children.Add(Widgets.Kpi("报送合计", UiText.Money(s.SubmittedTotal)));
        _kpis.Children.Add(Widgets.Kpi("确认合计", UiText.Money(s.ConfirmedTotal)));
        _kpis.Children.Add(Widgets.Kpi("差额（确认−报送）", UiText.Money(s.VarianceTotal), s.VarianceTotal < 0));
        _kpis.Children.Add(Widgets.Kpi("数据截至", UiText.Date(s.LatestAsOfDate)));

        await RenderVarianceAsync();

        _host.Children.Clear();
        if (s.Lines.Count == 0) { _host.Children.Add(Widgets.EmptyState("尚无可比的成果结果")); return; }

        var headers = new[] { "期次 / 批次", "类型", "控制项", "报送", "确认", "差额", "截至" };
        var right = new HashSet<int> { 3, 4, 5 };
        var rows = s.Lines.Select(l => new[]
        {
            l.Period, TypeName(l.Type), l.ControlItemName ?? "—",
            UiText.Money(l.SubmittedAmount), UiText.Money(l.ConfirmedAmount),
            UiText.Money(l.Variance), l.AsOfDate.ToString("yyyy-MM-dd")
        });
        _host.Children.Add(Widgets.Table(headers, rows, right, r => Parse(r[5]) < 0));
    }

    private async Task RenderVarianceAsync()
    {
        _varianceHost.Children.Clear();
        if (_current is null) return;
        var v = await _svc.SummarizeVarianceAsync(_current.Id);

        var rows = new List<string[]>
        {
            new[] { "报送合计（外部成果）", UiText.Money(v.SubmittedRevenue) },
            new[] { "确认合计（外部核对/审定）", UiText.Money(v.ConfirmedRevenue) },
            new[] { "项目预计收入（对上收入事实）", UiText.Money(v.ExpectedIncome) },
            new[] { "确认收入 − 报送收入", UiText.Money(v.RevenueVarianceVsSubmitted) },
            new[] { "确认收入 − 预计收入", UiText.Money(v.RevenueVarianceVsExpected) },
        };
        if (v.HasForecast)
        {
            rows.Add(new[] { "预计最终成本（预测版本）", UiText.Money(v.EstimatedFinalCost ?? 0m) });
            rows.Add(new[] { "目标成本（预测版本）", UiText.Money(v.TargetCost ?? 0m) });
        }
        else
        {
            rows.Add(new[] { "预计最终成本（预测版本）", "尚无预测版本" });
        }
        _varianceHost.Children.Add(Widgets.Table(new[] { "口径", "金额" }, rows, new HashSet<int> { 1 },
            r => r[0].Contains("−") && Parse(r[1]) < 0));
        _varianceHost.Children.Add(Widgets.Hint("三个口径不同，不跨口径相加：报送/确认是外部成果结果（收入侧），预计收入来自对上收入事实，预计最终成本来自成本人员预测版本。差额为管理提示，非财务结论。"));
    }

    private void OpenRegister()
    {
        if (_current is null) return;
        var period = new TextBox { Width = 400, Watermark = "期次 / 批次（如 2026-09 进度款）" };
        var type = new ComboBox { ItemsSource = TypePicks(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "类型", Width = 400 };
        var item = new ComboBox { ItemsSource = _items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "控制项（可空）", Width = 400 };
        var submitted = new NumericUpDown { Width = 400, FormatString = "N2", Increment = 1000, Watermark = "报送金额" };
        var confirmed = new NumericUpDown { Width = 400, FormatString = "N2", Increment = 1000, Watermark = "确认金额" };
        var asOf = new DatePicker { Width = 400, SelectedDate = new DateTimeOffset(DateTime.Today) };
        var source = new TextBox { Width = 400, Watermark = "来源（必填，成果文件名称或报送渠道）" };
        var by = new TextBox { Width = 400, Watermark = "提供方（可空）" };

        var body = new StackPanel { Spacing = 10 };
        foreach (var (l, c) in new (string, Control)[]
        {
            ("期次 / 批次", period), ("类型", type), ("控制项", item),
            ("报送金额", submitted), ("确认金额", confirmed),
            ("数据截至", asOf), ("来源", source), ("提供方", by)
        })
            body.Children.Add(Widgets.Row(l, c, 96));
        body.Children.Add(Widgets.Hint("只登记结果金额；成果文件由外部造价软件编制，本系统不生成、不保存计价依据与计算底稿。"));

        Widgets.OpenForm(this, "登记外部成果结果", "报送额与确认额相互独立，互不覆盖。", body, "登记", async () =>
        {
            if (type.SelectedItem is not Pick<ResultRecordType> tp) return "请选择类型。";
            if (string.IsNullOrWhiteSpace(source.Text)) return "必须填写来源。";
            var d = asOf.SelectedDate is DateTimeOffset dt ? DateOnly.FromDateTime(dt.DateTime) : DateOnly.FromDateTime(DateTime.Today);
            try
            {
                await _svc.RegisterAsync(new ExternalResultRecord
                {
                    ProjectId = _current.Id, Period = period.Text?.Trim() ?? "", Type = tp.Value,
                    CostControlItemId = (item.SelectedItem as Pick<int>)?.Value,
                    SubmittedAmount = submitted.Value ?? 0m, ConfirmedAmount = confirmed.Value ?? 0m,
                    AsOfDate = d, Source = source.Text!.Trim(),
                    ProvidedBy = string.IsNullOrWhiteSpace(by.Text) ? null : by.Text!.Trim()
                });
                await ReloadAsync();
                SetMsg("已登记外部成果结果。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    private static IReadOnlyList<Pick<ResultRecordType>> TypePicks() => new List<Pick<ResultRecordType>>
    {
        new(ResultRecordType.ProgressMeasurement, "进度款报量"),
        new(ResultRecordType.SiteMeasurement, "收方单"),
        new(ResultRecordType.AuditSettlement, "送审结算"),
        new(ResultRecordType.ApprovedSettlement, "审定结算"),
    };

    private static string TypeName(ResultRecordType t) => t switch
    {
        ResultRecordType.ProgressMeasurement => "进度款报量",
        ResultRecordType.SiteMeasurement => "收方单",
        ResultRecordType.AuditSettlement => "送审结算",
        ResultRecordType.ApprovedSettlement => "审定结算",
        _ => t.ToString()
    };

    private static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) ? v : 0m;

    private void SetMsg(string text, bool error = false)
    {
        _msg.Text = text;
        _msg.Foreground = error ? Palette.Red : Palette.Txt2;
    }
}
