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
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 变化事项（独立页，对应预览稿）：四个金额相互独立，互不覆盖。
/// 页面 = 页头 + 事项表；登记走弹层。
/// </summary>
public class ChangeItemsView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly CostForecastService _svc = null!;
    private Project? _current;
    private List<CostControlItem> _items = new();

    private readonly TextBlock _title = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
    private readonly StackPanel _host = new();
    private readonly IReadOnlyList<Pick<BusinessSegment>> _segments = UiText.Segments();

    public ChangeItemsView(AppDbContext db)
    {
        _db = db;
        _svc = new CostForecastService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        var top = Widgets.PageHeaderWithDynamicSubtitle("事项", "变化事项",
            "不编制签证单，只记录对成本与收入的影响", _title,
            Widgets.PrimaryActionButton("＋ 登记事项", () => OpenRegister()));

        var headers = new[] { "编号", "事项", "板块", "控制项", "我方测算", "对方报送", "外部确认", "纳入预测", "状态", "截至" };
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card("四个金额相互独立，互不覆盖（我方测算 ≠ 对方报送 ≠ 外部确认 ≠ 纳入预测）", _host));
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
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        _host.Children.Clear();
        var changes = await _svc.ListChangeItemsAsync(_current!.Id);
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
        _host.Children.Add(Widgets.Table(headers, rows, right));
    }

    private void OpenRegister()
    {
        if (_current is null) return;
        var code = new TextBox { Width = 400, Watermark = "事项编号" };
        var title = new TextBox { Width = 400, Watermark = "事项名称" };
        var segment = new ComboBox { ItemsSource = _segments, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块", Width = 400 };
        var status = new ComboBox { ItemsSource = UiText.ChangeStatuses(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "状态", Width = 400 };
        var item = new ComboBox { ItemsSource = _items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "控制项（可空）", Width = 400 };
        var estimate = new NumericUpDown { Width = 400, FormatString = "N2", Watermark = "我方测算" };
        var submitted = new NumericUpDown { Width = 400, FormatString = "N2", Watermark = "对方报送" };
        var confirmed = new NumericUpDown { Width = 400, FormatString = "N2", Watermark = "外部确认" };
        var included = new NumericUpDown { Width = 400, FormatString = "N2", Watermark = "纳入预测" };
        var asOf = new DatePicker { Width = 400, SelectedDate = new DateTimeOffset(DateTime.Today) };
        var source = new TextBox { Width = 400, Watermark = "来源（必填）" };
        var by = new TextBox { Width = 400, Watermark = "提供方（可空）" };

        var body = new StackPanel { Spacing = 10 };
        foreach (var (l, c) in new (string, Control)[]
        {
            ("事项编号", code), ("事项名称", title), ("业务板块", segment), ("控制项", item),
            ("我方测算", estimate), ("对方报送", submitted), ("外部确认", confirmed), ("纳入预测", included),
            ("状态", status), ("数据截至", asOf), ("来源", source), ("提供方", by)
        })
            body.Children.Add(Widgets.Row(l, c, 90));
        body.Children.Add(Widgets.Hint("对方报送不覆盖我方测算，我方测算也不能写成对方已确认。"));

        Widgets.OpenForm(this, "登记变化事项", "四金额独立录入。", body, "登记", async () =>
        {
            if (segment.SelectedItem is not Pick<BusinessSegment> seg) return "请选择业务板块。";
            if (status.SelectedItem is not Pick<ChangeItemStatus> st) return "请选择状态。";
            var d = asOf.SelectedDate is DateTimeOffset dt ? DateOnly.FromDateTime(dt.DateTime) : DateOnly.FromDateTime(DateTime.Today);
            try
            {
                await _svc.RegisterChangeItemAsync(new ChangeItem
                {
                    ProjectId = _current.Id, Code = code.Text?.Trim() ?? "", Title = title.Text?.Trim() ?? "",
                    Segment = seg.Value, CostControlItemId = (item.SelectedItem as Pick<int>)?.Value,
                    CostOfficerEstimate = estimate.Value, CounterpartySubmitted = submitted.Value,
                    ExternallyConfirmed = confirmed.Value, IncludedInForecast = included.Value,
                    Status = st.Value, AsOfDate = d, Source = source.Text?.Trim() ?? "",
                    ProvidedBy = string.IsNullOrWhiteSpace(by.Text) ? null : by.Text!.Trim()
                });
                await RenderAsync();
                SetMsg($"已登记变化事项。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    private void SetMsg(string text, bool error = false)
    {
        _msg.Text = text;
        _msg.Foreground = error ? Palette.Red : Palette.Txt2;
    }
}
