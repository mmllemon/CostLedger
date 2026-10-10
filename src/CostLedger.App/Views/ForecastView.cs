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
/// 成本预测版本与变化事项：页面只保留页头 + KPI + 版本历史 + 预测行表 + 变化事项表；
/// 新建版本 / 录入预测行 / 冻结 / 登记变化事项 全部改用弹层卡片。
/// 预计最终 = 已知 + 剩余估计 + 风险调整（派生值）；已知金额自动取、不接受手填。
/// </summary>
public class ForecastView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly CostForecastService _svc = null!;
    private Project? _current;
    private CostForecastSummary? _summary;
    private List<CostControlItem> _items = new();
    private List<CostForecastVersion> _versions = new();

    private readonly ComboBox _versionCombo = new() { Width = 300, PlaceholderText = "预测版本" };
    private readonly WrapPanel _kpiHost = new();
    private readonly StackPanel _lineHost = new(), _versionHost = new();
    private readonly TextBlock _msg = new();
    private readonly TextBlock _title = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _freezeBtn;

    public ForecastView(AppDbContext db)
    {
        _db = db;
        _svc = new CostForecastService(db);
        _freezeBtn = Widgets.ActionButton("冻结当前版本", () => OnFreeze());
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _versionCombo.DisplayMemberBinding = new Binding("Label");
        _versionCombo.SelectionChanged += async (_, _) =>
        {
            if (_current is null || _versionCombo.SelectedItem is not Pick<int> v) return;
            _summary = await _svc.GetSummaryAsync(_current.Id, v.Value);
            RenderSummary();
        };

        _msg.FontSize = 12.5;
        _msg.TextWrapping = TextWrapping.Wrap;
        _msg.Foreground = Palette.Txt2;

        var top = Widgets.PageHeaderWithDynamicSubtitle("预测", "成本预测版本",
            "预计最终 = 已知 + 剩余估计 + 风险调整（派生值）", _title,
            Widgets.PrimaryActionButton("＋ 新建版本", () => OpenCreateVersion()),
            Widgets.ActionButton("录入 / 修订预测行", () => OpenUpsertLine()),
            _freezeBtn);

        var versionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        versionRow.Children.Add(new TextBlock { Text = "查看版本", Foreground = Palette.Txt2, VerticalAlignment = VerticalAlignment.Center });
        versionRow.Children.Add(_versionCombo);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card(null, versionRow));
        root.Children.Add(Widgets.Card(null, _kpiHost));
        root.Children.Add(Widgets.Card("版本历史（修订产生新版本，冻结后不可改写）", _versionHost));
        root.Children.Add(Widgets.Card("按控制项的预测行", _lineHost));
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    /// <summary>由顶部全局项目切换器驱动。</summary>
    public void SelectProject(Project project)
    {
        _current = project;
        _ = ReloadAsync();
    }

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

        _versions = await _db.CostForecastVersions.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderByDescending(x => x.VersionNumber).ToListAsync();
        _versionCombo.ItemsSource = _versions.Select(v =>
            new Pick<int>(v.Id, $"v{v.VersionNumber} · {v.ForecastDate:yyyy-MM-dd} · {UiText.VersionStatus(v.Status)}")).ToList();

        RenderVersions();

        if (_versions.Count > 0) _versionCombo.SelectedIndex = 0;
        else { _summary = null; RenderSummary(); }
    }

    private void RenderVersions()
    {
        _versionHost.Children.Clear();
        var headers = new[] { "版本", "预测日期", "可信度", "状态", "编制假设" };
        var rows = _versions.Select(v => new[]
        {
            "v" + v.VersionNumber, v.ForecastDate.ToString("yyyy-MM-dd"),
            UiText.Confidence(v.Confidence), UiText.VersionStatus(v.Status), v.Basis ?? "—"
        });
        _versionHost.Children.Add(Widgets.Table(headers, rows));
    }

    private void RenderSummary()
    {
        var f = _summary;
        _kpiHost.Children.Clear();
        _lineHost.Children.Clear();

        if (f is null)
        {
            _kpiHost.Children.Add(new TextBlock { Text = "尚无预测版本。点击「新建版本」开始。", FontSize = 13, Foreground = Palette.Txt3, Margin = new Thickness(0, 4, 0, 4) });
            _freezeBtn.IsEnabled = false;
            return;
        }

        _freezeBtn.IsEnabled = f.Status != ForecastVersionStatus.Frozen;

        void Kpi(string l, string v, bool bad = false) => _kpiHost.Children.Add(Widgets.Kpi(l, v, bad, 176));
        Kpi("目标成本", UiText.Money(f.TargetTotal));
        Kpi("已知金额（自动取）", UiText.Money(f.KnownTotal));
        Kpi("剩余估计", UiText.Money(f.RemainingTotal));
        Kpi("风险调整", UiText.Money(f.RiskTotal));
        Kpi("预计最终成本", UiText.Money(f.EstimatedFinalTotal), f.VarianceTotal > 0);
        Kpi("预计超目标", UiText.Money(f.VarianceTotal), f.VarianceTotal > 0);
        Kpi("未归属金额", UiText.Money(f.UnallocatedAmount), f.UnallocatedAmount > 0);
        Kpi("未纳入预测的变化", UiText.Money(f.UnincludedChangeAmount), f.UnincludedChangeAmount > 0);
        Kpi("可信度", UiText.Confidence(f.Confidence));
        Kpi("状态", UiText.VersionStatus(f.Status));

        var headers = new[] { "控制项", "目标", "已知(自动)", "剩余估计", "风险调整", "预计最终", "偏差", "精度封顶" };
        var right = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var rows = f.Lines.Select(l => new[]
        {
            l.Code + " " + l.Name,
            UiText.Money(l.TargetAmount), UiText.Money(l.KnownAmount),
            UiText.Money(l.RemainingEstimate), UiText.Money(l.RiskAdjustment),
            UiText.Money(l.EstimatedFinal), UiText.Money(l.Variance), UiText.Precision(l.PrecisionCap)
        });
        _lineHost.Children.Add(Widgets.Table(headers, rows, right, r => Parse(r[6]) > 0));
    }

    // ==================== 弹层：新建版本 ====================
    private void OpenCreateVersion()
    {
        if (_current is null) return;
        var date = new DatePicker { Width = 400, SelectedDate = new DateTimeOffset(DateTime.Today) };
        var confidence = new ComboBox { ItemsSource = UiText.Confidences(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "可信度", Width = 400 };
        var basis = new TextBox { Watermark = "编制假设（价格水平、剩余量依据、未确认事项）", Width = 400, AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("预测日期", date, 90));
        body.Children.Add(Widgets.Row("可信度", confidence, 90));
        body.Children.Add(Widgets.Row("编制假设", basis, 90));

        Widgets.OpenForm(this, "新建预测版本", "修订请新建版本；冻结后不可改写。", body, "新建版本", async () =>
        {
            var d = date.SelectedDate is DateTimeOffset dt ? DateOnly.FromDateTime(dt.DateTime) : DateOnly.FromDateTime(DateTime.Today);
            var conf = (confidence.SelectedItem as Pick<ForecastConfidence>)?.Value ?? ForecastConfidence.Low;
            try
            {
                var v = await _svc.CreateVersionAsync(_current.Id, d, conf, string.IsNullOrWhiteSpace(basis.Text) ? null : basis.Text!.Trim());
                await ReloadAsync();
                SetMsg($"已新建预测版本 v{v.VersionNumber}（草稿）。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    // ==================== 弹层：录入 / 修订预测行 ====================
    private void OpenUpsertLine()
    {
        if (_current is null || _summary is null) { SetMsg("请先新建草稿版本。", true); return; }
        if (_summary.Status == ForecastVersionStatus.Frozen) { SetMsg("当前版本已冻结，请新建版本再修订。", true); return; }

        var item = new ComboBox { ItemsSource = _items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "控制项", Width = 400 };
        var target = new NumericUpDown { Width = 400, FormatString = "N2", Increment = 1000, Watermark = "目标成本" };
        var remaining = new NumericUpDown { Width = 400, FormatString = "N2", Increment = 1000, Watermark = "剩余估计" };
        var risk = new NumericUpDown { Width = 400, FormatString = "N2", Increment = 1000, Watermark = "风险调整" };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("控制项", item, 90));
        body.Children.Add(Widgets.Row("目标成本", target, 90));
        body.Children.Add(Widgets.Row("剩余估计", remaining, 90));
        body.Children.Add(Widgets.Row("风险调整", risk, 90));
        body.Children.Add(Widgets.Hint("已知金额由系统按归属关系自动计算，不接受手填。"));

        Widgets.OpenForm(this, "录入 / 修订预测行", $"当前版本 v{_summary.VersionNumber}（{UiText.VersionStatus(_summary.Status)}）。", body, "保存此行", async () =>
        {
            if (item.SelectedItem is not Pick<int> it) return "请选择控制项。";
            try
            {
                await _svc.UpsertLineAsync(_summary.VersionId, it.Value, target.Value ?? 0m, remaining.Value ?? 0m, risk.Value ?? 0m);
                await ReloadAsync();
                SetMsg("已保存预测行；已知金额按当前归属自动重算。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    private async void OnFreeze()
    {
        if (_summary is null) return;
        try
        {
            var v = await _svc.FreezeAsync(_summary.VersionId);
            await ReloadAsync();
            SetMsg($"已冻结版本 v{v.VersionNumber}；此后修订请新建版本。");
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) ? v : 0m;

    private void SetMsg(string text, bool error = false)
    {
        _msg.Text = text;
        _msg.Foreground = error ? Palette.Red : Palette.Txt2;
    }
}
