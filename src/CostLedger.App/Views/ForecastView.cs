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
/// 成本预测版本与变化事项：新建版本 → 按控制项录剩余估计与风险调整 → 冻结。
/// 预计最终 = 已知 + 剩余估计 + 风险调整（派生值，不是录入项）；已知金额由归属关系自动取，不接受手填。
/// 变化事项四个金额相互独立，不互相覆盖。
/// </summary>
public class ForecastView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly CostForecastService _svc = null!;
    private Project? _current;
    private CostForecastSummary? _summary;
    private List<CostControlItem> _items = new();

    private ComboBox _versionCombo = null!;
    private WrapPanel _kpiHost = null!;
    private StackPanel _lineHost = null!, _versionHost = null!, _changeHost = null!;
    private TextBlock _msg = null!;

    private DatePicker _newDate = null!;
    private ComboBox _newConfidence = null!;
    private TextBox _newBasis = null!;
    private ComboBox _lineItem = null!;
    private NumericUpDown _lineTarget = null!, _lineRemaining = null!, _lineRisk = null!;
    private Button _freezeBtn = null!;
    private TextBox _chCode = null!, _chTitle = null!, _chSource = null!, _chBy = null!;
    private ComboBox _chSegment = null!, _chItem = null!, _chStatus = null!;
    private NumericUpDown _chEstimate = null!, _chSubmitted = null!, _chConfirmed = null!, _chIncluded = null!;
    private DatePicker _chAsOf = null!;

    public ForecastView(AppDbContext db)
    {
        _db = db;
        _svc = new CostForecastService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _versionCombo = new ComboBox { Width = 280, PlaceholderText = "预测版本" };
        _versionCombo.DisplayMemberBinding = new Binding("Label");
        _versionCombo.SelectionChanged += async (_, _) =>
        {
            if (_current is null || _versionCombo.SelectedItem is not Pick<int> v) return;
            _summary = await _svc.GetSummaryAsync(_current.Id, v.Value);
            RenderSummary();
        };

        _msg = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
        _kpiHost = new WrapPanel();
        _lineHost = new StackPanel();
        _versionHost = new StackPanel();
        _changeHost = new StackPanel();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(new TextBlock { Text = "版本", VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Txt2 });
        top.Children.Add(_versionCombo);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        root.Children.Add(top);
        root.Children.Add(BuildVersionSection());
        root.Children.Add(Widgets.Card(null, _kpiHost));
        root.Children.Add(Widgets.Card("版本历史（修订产生新版本，冻结后不可改写）", _versionHost));
        root.Children.Add(BuildLineSection());
        root.Children.Add(BuildChangeSection());
        root.Children.Add(_msg);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private Control BuildVersionSection()
    {
        _newDate = new DatePicker { Width = 150, SelectedDate = new DateTimeOffset(DateTime.Today) };
        _newConfidence = new ComboBox { ItemsSource = UiText.Confidences(), Width = 110, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "可信度" };
        _newBasis = new TextBox { Watermark = "编制假设（价格水平、剩余量依据、未确认事项）", Width = 360 };
        var create = new Button { Content = "新建版本（草稿）" };
        create.Classes.Add("primary");
        create.Click += OnCreateVersion;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_newDate); row.Children.Add(_newConfidence); row.Children.Add(_newBasis); row.Children.Add(create);
        return Widgets.Card("新建预测版本", row);
    }

    private Control BuildLineSection()
    {
        _lineItem = new ComboBox { Width = 220, PlaceholderText = "控制项" };
        _lineItem.DisplayMemberBinding = new Binding("Label");
        _lineTarget = new NumericUpDown { Width = 140, FormatString = "N2", Increment = 1000, Watermark = "目标成本" };
        _lineRemaining = new NumericUpDown { Width = 140, FormatString = "N2", Increment = 1000, Watermark = "剩余估计" };
        _lineRisk = new NumericUpDown { Width = 140, FormatString = "N2", Increment = 1000, Watermark = "风险调整" };
        var save = new Button { Content = "录入 / 修订此行" };
        save.Classes.Add("primary");
        save.Click += OnUpsertLine;
        _freezeBtn = new Button { Content = "冻结当前版本" };
        _freezeBtn.Classes.Add("pill");
        _freezeBtn.Click += OnFreeze;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(_lineItem); row.Children.Add(_lineTarget); row.Children.Add(_lineRemaining); row.Children.Add(_lineRisk);
        row.Children.Add(save); row.Children.Add(_freezeBtn);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(row);
        stack.Children.Add(new ScrollViewer { Content = _lineHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 320 });
        stack.Children.Add(Widgets.Hint("目标 / 剩余估计 / 风险调整由成本人员填写；已知金额由系统按归属关系自动计算。冻结必须写明编制假设并至少有一条预测行。"));
        return Widgets.Card("按控制项录入预测", stack);
    }

    private Control BuildChangeSection()
    {
        _chCode = new TextBox { Watermark = "事项编号", Width = 90 };
        _chTitle = new TextBox { Watermark = "事项名称", Width = 180 };
        _chSegment = new ComboBox { ItemsSource = UiText.Segments(), Width = 130, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块" };
        _chItem = new ComboBox { Width = 180, PlaceholderText = "控制项（可空）" };
        _chItem.DisplayMemberBinding = new Binding("Label");
        _chEstimate = new NumericUpDown { Watermark = "我方测算", Width = 120, FormatString = "N2" };
        _chSubmitted = new NumericUpDown { Watermark = "对方报送", Width = 120, FormatString = "N2" };
        _chConfirmed = new NumericUpDown { Watermark = "外部确认", Width = 120, FormatString = "N2" };
        _chIncluded = new NumericUpDown { Watermark = "纳入预测", Width = 120, FormatString = "N2" };
        _chStatus = new ComboBox { ItemsSource = UiText.ChangeStatuses(), Width = 130, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "状态" };
        _chAsOf = new DatePicker { Width = 150, SelectedDate = new DateTimeOffset(DateTime.Today) };
        _chSource = new TextBox { Watermark = "来源（必填）", Width = 200 };
        _chBy = new TextBox { Watermark = "提供方", Width = 120 };
        var save = new Button { Content = "登记变化事项" };
        save.Classes.Add("primary");
        save.Click += OnRegisterChange;

        var form = new StackPanel { Spacing = 8 };
        var r1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        r1.Children.Add(_chCode); r1.Children.Add(_chTitle); r1.Children.Add(_chSegment); r1.Children.Add(_chStatus);
        var r2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        r2.Children.Add(_chEstimate); r2.Children.Add(_chSubmitted); r2.Children.Add(_chConfirmed); r2.Children.Add(_chIncluded);
        var r3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        r3.Children.Add(_chItem); r3.Children.Add(_chAsOf); r3.Children.Add(_chSource); r3.Children.Add(_chBy); r3.Children.Add(save);
        form.Children.Add(r1); form.Children.Add(r2); form.Children.Add(r3);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(form);
        stack.Children.Add(new ScrollViewer { Content = _changeHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 320 });
        stack.Children.Add(Widgets.Hint("四个金额相互独立、互不覆盖：对方报送不覆盖我方测算，我方测算也不能写成对方已确认。尚未测算不能填纳入金额；标记不纳入时纳入金额必须为空。"));
        return Widgets.Card("变化事项（不编制签证单，只记录对成本与收入的影响）", stack);
    }

    /// <summary>由顶部全局项目切换器驱动。</summary>
    public void SelectProject(Project project)
    {
        _current = project;
        _ = ReloadAsync();
    }

    private async Task InitAsync()
    {
        // 项目由顶部全局切换器统一驱动，避免页内出现第二个项目下拉。
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        if (_current is null && projects.Count > 0) { _current = projects[0]; await ReloadAsync(); }
    }

    private async Task ReloadAsync()
    {
        if (_current is null) return;
        _items = await _db.CostControlItems.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync();
        var picks = _items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList();
        _lineItem.ItemsSource = picks;
        _chItem.ItemsSource = picks;

        var versions = await _db.CostForecastVersions.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderByDescending(x => x.VersionNumber).ToListAsync();
        _versionCombo.ItemsSource = versions.Select(v =>
            new Pick<int>(v.Id, $"v{v.VersionNumber} · {v.ForecastDate:yyyy-MM-dd} · {UiText.VersionStatus(v.Status)}")).ToList();

        RenderVersions(versions);
        await RenderChangesAsync();

        if (versions.Count > 0) _versionCombo.SelectedIndex = 0;
        else { _summary = null; RenderSummary(); }
    }

    private void RenderVersions(List<CostForecastVersion> versions)
    {
        _versionHost.Children.Clear();
        var headers = new[] { "版本", "预测日期", "可信度", "状态", "编制假设" };
        var rows = versions.Select(v => new[]
        {
            "v" + v.VersionNumber, v.ForecastDate.ToString("yyyy-MM-dd"),
            UiText.Confidence(v.Confidence), UiText.VersionStatus(v.Status), v.Basis ?? "—"
        });
        _versionHost.Children.Add(Widgets.Table(headers, rows));
    }

    private async Task RenderChangesAsync()
    {
        if (_current is null) return;
        _changeHost.Children.Clear();
        var changes = await _svc.ListChangeItemsAsync(_current.Id);
        var headers = new[] { "编号", "事项", "板块", "控制项", "我方测算", "对方报送", "外部确认", "纳入预测", "状态", "截至" };
        var right = new HashSet<int> { 4, 5, 6, 7 };
        var rows = changes.Select(c => new[]
        {
            c.Code, c.Title, UiText.Segment(c.Segment),
            _items.FirstOrDefault(i => i.Id == c.CostControlItemId)?.Name ?? "—",
            c.CostOfficerEstimate is decimal a ? UiText.Money(a) : "—",
            c.CounterpartySubmitted is decimal b ? UiText.Money(b) : "—",
            c.ExternallyConfirmed is decimal d ? UiText.Money(d) : "—",
            c.IncludedInForecast is decimal e ? UiText.Money(e) : "未纳入",
            UiText.ChangeStatus(c.Status), c.AsOfDate.ToString("yyyy-MM-dd")
        });
        _changeHost.Children.Add(Widgets.Table(headers, rows, right));
    }

    private void RenderSummary()
    {
        var f = _summary;
        _kpiHost.Children.Clear();
        _lineHost.Children.Clear();

        if (f is null)
        {
            _kpiHost.Children.Add(new TextBlock { Text = "尚无预测版本。请先新建版本并录入预测行。", FontSize = 13, Foreground = Palette.Txt3, Margin = new Thickness(0, 4, 0, 4) });
            _lineTarget.IsEnabled = _lineRemaining.IsEnabled = _lineRisk.IsEnabled = false;
            _freezeBtn.IsEnabled = false;
            return;
        }

        var editable = f.Status != ForecastVersionStatus.Frozen;
        _lineTarget.IsEnabled = _lineRemaining.IsEnabled = _lineRisk.IsEnabled = editable;
        _freezeBtn.IsEnabled = editable;

        void Kpi(string l, string v, bool bad = false) => _kpiHost.Children.Add(Widgets.Kpi(l, v, bad));
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

    private async void OnCreateVersion(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        var date = _newDate.SelectedDate is DateTimeOffset d ? DateOnly.FromDateTime(d.DateTime) : DateOnly.FromDateTime(DateTime.Today);
        var conf = (_newConfidence.SelectedItem as Pick<ForecastConfidence>)?.Value ?? ForecastConfidence.Low;
        try
        {
            var v = await _svc.CreateVersionAsync(_current.Id, date, conf, string.IsNullOrWhiteSpace(_newBasis.Text) ? null : _newBasis.Text!.Trim());
            SetMsg($"已新建预测版本 v{v.VersionNumber}（草稿）。");
            await ReloadAsync();
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private async void OnUpsertLine(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null || _summary is null) { SetMsg("请先新建草稿版本。", true); return; }
        if (_lineItem.SelectedItem is not Pick<int> item) { SetMsg("请选择控制项。", true); return; }
        try
        {
            await _svc.UpsertLineAsync(_summary.VersionId, item.Value,
                _lineTarget.Value ?? 0m, _lineRemaining.Value ?? 0m, _lineRisk.Value ?? 0m);
            SetMsg("已保存预测行；已知金额按当前归属自动重算。");
            await ReloadAsync();
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private async void OnFreeze(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_summary is null) return;
        try
        {
            var v = await _svc.FreezeAsync(_summary.VersionId);
            SetMsg($"已冻结版本 v{v.VersionNumber}；此后修订请新建版本。");
            await ReloadAsync();
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private async void OnRegisterChange(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_chSegment.SelectedItem is not Pick<BusinessSegment> seg) { SetMsg("请选择变化事项所属业务板块。", true); return; }
        if (_chStatus.SelectedItem is not Pick<ChangeItemStatus> st) { SetMsg("请选择变化事项状态。", true); return; }
        var asOf = _chAsOf.SelectedDate is DateTimeOffset d ? DateOnly.FromDateTime(d.DateTime) : DateOnly.FromDateTime(DateTime.Today);
        var item = new ChangeItem
        {
            ProjectId = _current.Id,
            Code = _chCode.Text?.Trim() ?? "",
            Title = _chTitle.Text?.Trim() ?? "",
            Segment = seg.Value,
            CostControlItemId = (_chItem.SelectedItem as Pick<int>)?.Value,
            CostOfficerEstimate = _chEstimate.Value,
            CounterpartySubmitted = _chSubmitted.Value,
            ExternallyConfirmed = _chConfirmed.Value,
            IncludedInForecast = _chIncluded.Value,
            Status = st.Value,
            AsOfDate = asOf,
            Source = _chSource.Text?.Trim() ?? "",
            ProvidedBy = string.IsNullOrWhiteSpace(_chBy.Text) ? null : _chBy.Text!.Trim()
        };
        try
        {
            await _svc.RegisterChangeItemAsync(item);
            SetMsg($"已登记变化事项 {item.Code}。");
            _chCode.Text = _chTitle.Text = "";
            _chEstimate.Value = _chSubmitted.Value = _chConfirmed.Value = _chIncluded.Value = null;
            await RenderChangesAsync();
            if (_summary != null) { _summary = await _svc.GetSummaryAsync(_current.Id, _summary.VersionId); RenderSummary(); }
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
