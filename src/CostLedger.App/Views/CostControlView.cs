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
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 成本控制项与归属：页面只保留页头动作按钮 + 控制项表；
/// 建控制项 / 登记业务关系 / 登记外部金额事实 / 分配 全部改用弹层卡片，
/// 避免录入表单常驻页面把信息层级冲散。规则一律经 EconomicFactsService。
/// </summary>
public class CostControlView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly EconomicFactsService _svc = null!;
    private Project? _current;

    private readonly StackPanel _itemHost = new();
    private readonly StackPanel _allocHost = new();
    private readonly TextBlock _msg = new();
    private readonly TextBlock _title = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly IReadOnlyList<Pick<BusinessSegment>> _segments = UiText.Segments();
    private readonly IReadOnlyList<Pick<EconomicDirection>> _directions = UiText.Directions();
    private readonly IReadOnlyList<Pick<ExternalFactStatus>> _statuses = UiText.Statuses();
    private readonly IReadOnlyList<Pick<ManagementPrecision>> _precisions = UiText.Precisions();

    private List<Party> _parties = new();
    private List<CostControlItem> _items = new();
    private List<ExternalAmountFact> _facts = new();
    private List<string> _roles = new();

    public CostControlView(AppDbContext db)
    {
        _db = db;
        _svc = new EconomicFactsService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _msg.FontSize = 12.5;
        _msg.TextWrapping = TextWrapping.Wrap;
        _msg.Foreground = Palette.Txt2;

        var top = Widgets.PageHeaderWithDynamicSubtitle("口径", "成本控制项与归属",
            "过程管理口径：不要求与合同清单逐项对齐", _title,
            Widgets.PrimaryActionButton("＋ 新建控制项", () => OpenAddItem()),
            Widgets.ActionButton("分配到控制项", () => OpenAllocate()),
            Widgets.ActionButton("登记外部金额", () => OpenRegisterFact()),
            Widgets.ActionButton("登记业务关系", () => OpenRegisterRelationship()));

        var itemCard = Widgets.Card("成本控制项汇总（汇总结论的可信颗粒度取该控制项所有来源中最粗的一档）", _itemHost);
        var allocCard = Widgets.Card("分配明细流水（更正 = 追加反向冲销，原分配不被删除或改写）", _allocHost);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(itemCard);
        root.Children.Add(allocCard);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    /// <summary>由顶部全局项目切换器驱动。</summary>
    public void SelectProject(Project project)
    {
        _current = project;
        _ = RefreshAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        _parties = await _db.Parties.OrderBy(p => p.Name).ToListAsync();
        _roles = await _svc.ListRoleCodesAsync();
        if (_current is null && projects.Count > 0) { _current = projects[0]; await RefreshAsync(); }
    }

    private async Task RefreshAsync()
    {
        if (_current is null) return;
        _title.Text = $"{_current.Code} · {_current.Name}";
        _items = await _db.CostControlItems.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync();
        _facts = await _db.ExternalAmountFacts.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderByDescending(x => x.AsOfDate).ToListAsync();

        var summary = await _svc.SummarizeControlItemsAsync(_current.Id);
        RenderItems(summary);
        await RenderAllocationsAsync();
        _msg.Text = summary.Count == 0 ? "该项目还没有已归属金额。" : "";
    }

    private async Task RenderAllocationsAsync()
    {
        _allocHost.Children.Clear();
        if (_current is null) return;
        var allocs = await _svc.ListAllocationsAsync(_current.Id);
        if (allocs.Count == 0)
        {
            _allocHost.Children.Add(Widgets.EmptyState("尚无分配记录"));
            return;
        }

        var reversedIds = allocs.Where(a => a.ReversedAllocationId.HasValue)
            .Select(a => a.ReversedAllocationId!.Value).ToHashSet();

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(AllocHeader());
        for (int i = 0; i < allocs.Count; i++)
            panel.Children.Add(AllocRow(allocs[i], i % 2 == 1, reversedIds.Contains(allocs[i].Id)));
        _allocHost.Children.Add(panel);
    }

    private static Control AllocHeader()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,*,Auto,Auto") };
        foreach (var (t, c) in new[] { ("来源依据", 0), ("目标控制项", 1), ("分配金额", 2), ("状态", 3), ("操作", 4) })
        {
            var isRight = c == 2;
            var tb = new TextBlock
            {
                Text = t,
                FontSize = 11.5,
                FontWeight = FontWeight.Bold,
                Foreground = Palette.Txt3,
                LetterSpacing = 0.5,
                TextAlignment = isRight ? TextAlignment.Right : TextAlignment.Left
            };
            Grid.SetColumn(tb, c);
            g.Children.Add(tb);
        }
        return new Border
        {
            Child = g,
            Padding = new Thickness(12, 9),
            Background = new SolidColorBrush(Color.Parse("#F7F2EB")),
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private Control AllocRow(ManagementAllocation a, bool alt, bool reversed)
    {
        var isReversal = a.Amount < 0;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,*,Auto,Auto") };

        var src = new TextBlock { Text = a.ExternalAmountFact?.Source ?? a.ExternalAmountFactId.ToString(), FontSize = 12.5, Foreground = Palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var item = new TextBlock { Text = a.CostControlItem is null ? a.CostControlItemId.ToString() : $"{a.CostControlItem.Code} {a.CostControlItem.Name}", FontSize = 12.5, Foreground = Palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var amt = new TextBlock
        {
            Text = UiText.Money(a.Amount),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = isReversal ? Palette.Red : Palette.Ink,
            TextAlignment = TextAlignment.Right,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Consolas, sans-serif"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var st = new TextBlock { Text = isReversal ? "冲销" : (reversed ? "已冲销" : UiText.AllocationStatus(a.Status)), FontSize = 11.5, Foreground = isReversal || reversed ? Palette.Red : Palette.Txt2, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(src, 0); Grid.SetColumn(item, 1); Grid.SetColumn(amt, 2); Grid.SetColumn(st, 3);
        g.Children.Add(src); g.Children.Add(item); g.Children.Add(amt); g.Children.Add(st);

        // 仅正向且未被冲销的行可冲销
        if (!isReversal && !reversed)
        {
            var btn = new Button { Content = "冲销", FontSize = 11.5, Padding = new Thickness(8, 3) };
            btn.Classes.Add("pill");
            btn.Click += (_, _) => OpenReverse(a);
            Grid.SetColumn(btn, 4);
            g.Children.Add(btn);
        }

        return new Border
        {
            Child = g,
            Padding = new Thickness(12, 10),
            Background = alt ? new SolidColorBrush(Color.Parse("#FCFAF7")) : Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private void OpenReverse(ManagementAllocation a)
    {
        if (_current is null) return;
        var reason = new TextBox { Width = 400, Watermark = "冲销原因（可空）" };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = $"将对这条分配追加等额反向冲销（{UiText.Money(-a.Amount)}）；原记录保留不改写。", FontSize = 12.5, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Widgets.Row("冲销原因", reason, 84));

        Widgets.OpenForm(this, "冲销分配", "更正方式为追加反向冲销，不删除原记录。", body, "冲销", async () =>
        {
            try
            {
                await _svc.ReverseAllocationAsync(a.Id, reason.Text);
                await RefreshAsync();
                SetMsg("已追加反向冲销；原分配保留。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    private void RenderItems(List<CostControlItemSummary> summary)
    {
        _itemHost.Children.Clear();
        var byId = summary.ToDictionary(x => x.CostControlItemId);
        var headers = new[] { "编号", "控制项", "已分配", "暂估", "已核对", "最粗来源精度", "来源数" };
        var right = new HashSet<int> { 2, 3, 4, 6 };
        var rows = _items.Select(i =>
        {
            byId.TryGetValue(i.Id, out var s);
            return new[]
            {
                i.Code,
                i.Name,
                UiText.Money(s?.AllocatedAmount ?? 0m),
                UiText.Money(s?.EstimatedAmount ?? 0m),
                UiText.Money(s?.CheckedAmount ?? 0m),
                s is null ? "—" : UiText.Precision(s.CoarsestSourcePrecision),
                (s?.SourceCount ?? 0).ToString()
            };
        });
        _itemHost.Children.Add(Widgets.Table(headers, rows, right));
    }

    // ==================== 弹层：新建控制项 ====================
    private void OpenAddItem()
    {
        if (_current is null) return;
        var code = new TextBox { Watermark = "控制项编号", Width = 460 };
        var name = new TextBox { Watermark = "控制项名称", Width = 460 };
        var sort = new TextBox { Watermark = "排序（数字）", Width = 460, Text = "0" };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("控制项编号", code, 90));
        body.Children.Add(Widgets.Row("控制项名称", name, 90));
        body.Children.Add(Widgets.Row("排序", sort, 90));

        Widgets.OpenForm(this, "新建成本控制项", "编号与名称必填，编号在项目内唯一。", body, "新建", async () =>
        {
            try
            {
                int.TryParse(sort.Text, out var s);
                await _svc.CreateControlItemAsync(_current.Id, code.Text ?? "", name.Text ?? "", s);
                await RefreshAsync();
                SetMsg($"已新建控制项 {code.Text?.Trim()} {name.Text?.Trim()}。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    // ==================== 弹层：登记业务关系 ====================
    private void OpenRegisterRelationship()
    {
        if (_current is null) return;
        var party = new ComboBox { ItemsSource = _parties, DisplayMemberBinding = new Binding("Name"), PlaceholderText = "对方单位", Width = 360 };
        var role = new ComboBox { ItemsSource = _roles, PlaceholderText = "角色（字典）", Width = 360 };
        var segment = new ComboBox { ItemsSource = _segments, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块", Width = 360 };
        var direction = new ComboBox { ItemsSource = _directions, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "金额方向", Width = 360 };
        var contract = new TextBox { Watermark = "合同编号（可空）", Width = 360 };
        var note = new TextBox { Watermark = "备注（可空）", Width = 360 };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("对方单位", party, 90));
        body.Children.Add(Widgets.Row("角色", role, 90));
        body.Children.Add(Widgets.Row("业务板块", segment, 90));
        body.Children.Add(Widgets.Row("金额方向", direction, 90));
        body.Children.Add(Widgets.Row("合同编号", contract, 90));
        body.Children.Add(Widgets.Row("备注", note, 90));
        body.Children.Add(Widgets.Hint("方向由板块决定：资金状态只允许已收/已付；对上收入只允许收入/中性；材料·劳务·分包·服务·间接费只允许支出/中性。"));

        Widgets.OpenForm(this, "登记业务关系", "同一单位可在一个项目里承担多个板块角色。", body, "登记", async () =>
        {
            if (party.SelectedItem is not Party p) return "请选择对方单位。";
            if (role.SelectedItem is not string roleCode || roleCode.Length == 0) return "请选择角色。";
            if (segment.SelectedItem is not Pick<BusinessSegment> seg) return "请选择业务板块。";
            if (direction.SelectedItem is not Pick<EconomicDirection> dir) return "请选择金额方向。";

            int? contractId = null;
            var ccode = contract.Text?.Trim();
            if (!string.IsNullOrEmpty(ccode))
            {
                var c = await _db.Contracts.FirstOrDefaultAsync(x => x.ProjectId == _current.Id && x.Code == ccode);
                if (c is null) return $"本项目未找到合同编号 {ccode}。";
                contractId = c.Id;
            }
            try
            {
                await _svc.RegisterRelationshipAsync(new ProjectPartyRelationship
                {
                    ProjectId = _current.Id, PartyId = p.Id, ContractId = contractId,
                    RoleCode = roleCode, Segment = seg.Value, Direction = dir.Value, Note = note.Text
                });
                SetMsg($"已登记：{p.Name} 在「{UiText.Segment(seg.Value)}」承担 {roleCode}（{UiText.Direction(dir.Value)}）。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    // ==================== 弹层：登记外部金额事实 ====================
    private void OpenRegisterFact()
    {
        if (_current is null) return;
        var segment = new ComboBox { ItemsSource = _segments, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块", Width = 360 };
        var direction = new ComboBox { ItemsSource = _directions, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "金额方向", Width = 360 };
        var amount = new NumericUpDown { Width = 360, FormatString = "N2", Increment = 1000 };
        var party = new ComboBox { ItemsSource = _parties, DisplayMemberBinding = new Binding("Name"), PlaceholderText = "对方单位（可空）", Width = 360 };
        var source = new TextBox { Watermark = "来源（必填，如合同号/台账/日报）", Width = 360 };
        var by = new TextBox { Watermark = "提供方（可空）", Width = 360 };
        var asOf = new DatePicker { Width = 360, SelectedDate = new DateTimeOffset(DateTime.Today) };
        var status = new ComboBox { ItemsSource = _statuses, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "数据状态", Width = 360 };
        var precision = new ComboBox { ItemsSource = _precisions, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "管理精度", Width = 360 };
        var desc = new TextBox { Watermark = "说明（可空）", Width = 360 };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("业务板块", segment, 90));
        body.Children.Add(Widgets.Row("金额方向", direction, 90));
        body.Children.Add(Widgets.Row("金额", amount, 90));
        body.Children.Add(Widgets.Row("对方单位", party, 90));
        body.Children.Add(Widgets.Row("来源", source, 90));
        body.Children.Add(Widgets.Row("提供方", by, 90));
        body.Children.Add(Widgets.Row("数据截至", asOf, 90));
        body.Children.Add(Widgets.Row("数据状态", status, 90));
        body.Children.Add(Widgets.Row("管理精度", precision, 90));
        body.Children.Add(Widgets.Row("说明", desc, 90));
        body.Children.Add(Widgets.Hint("只记录你获知的金额、来源与截至日期；挂对方单位时方向沿用其已建立的关系。"));

        Widgets.OpenForm(this, "登记外部金额事实", "系统不判定财务是否入账。", body, "登记", async () =>
        {
            if (segment.SelectedItem is not Pick<BusinessSegment> seg) return "请选择业务板块。";
            if (direction.SelectedItem is not Pick<EconomicDirection> dir) return "请选择金额方向。";
            if (status.SelectedItem is not Pick<ExternalFactStatus> st) return "请选择数据状态。";
            if (precision.SelectedItem is not Pick<ManagementPrecision> pr) return "请选择管理精度。";
            if (string.IsNullOrWhiteSpace(source.Text)) return "外部金额事实必须填写来源。";
            var asOfDate = asOf.SelectedDate is DateTimeOffset d ? DateOnly.FromDateTime(d.DateTime) : DateOnly.FromDateTime(DateTime.Today);
            try
            {
                await _svc.RegisterFactAsync(new ExternalAmountFact
                {
                    ProjectId = _current.Id, Segment = seg.Value, Direction = dir.Value,
                    Amount = amount.Value ?? 0m, PartyId = (party.SelectedItem as Party)?.Id,
                    Source = source.Text!.Trim(), ProvidedBy = string.IsNullOrWhiteSpace(by.Text) ? null : by.Text!.Trim(),
                    AsOfDate = asOfDate, Status = st.Value, Precision = pr.Value,
                    Description = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text!.Trim()
                });
                await RefreshAsync();
                SetMsg($"已登记外部金额事实。");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
    }

    // ==================== 弹层：分配到控制项 ====================
    private void OpenAllocate()
    {
        if (_current is null) return;
        var factPicks = _facts.Select(x => new Pick<int>(x.Id, $"{UiText.Segment(x.Segment)} · {UiText.Money(x.Amount)} · {x.Source}")).ToList();
        var itemPicks = _items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList();

        var fact = new ComboBox { ItemsSource = factPicks, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "来源金额事实", Width = 360 };
        var item = new ComboBox { ItemsSource = itemPicks, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "归属控制项", Width = 360 };
        var amount = new NumericUpDown { Width = 360, FormatString = "N2", Increment = 1000 };
        var precision = new ComboBox { ItemsSource = _precisions, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "分配精度", Width = 360 };
        var status = new ComboBox { ItemsSource = UiText.AllocationStatuses(), DisplayMemberBinding = new Binding("Label"), PlaceholderText = "状态", Width = 360 };
        var note = new TextBox { Watermark = "备注（可空）", Width = 360 };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("来源金额事实", fact, 96));
        body.Children.Add(Widgets.Row("归属控制项", item, 96));
        body.Children.Add(Widgets.Row("分配金额", amount, 96));
        body.Children.Add(Widgets.Row("分配精度", precision, 96));
        body.Children.Add(Widgets.Row("状态", status, 96));
        body.Children.Add(Widgets.Row("备注", note, 96));
        body.Children.Add(Widgets.Hint("允许一笔来源拆到多个控制项、允许暂估与不分配；分配累计不得超过来源金额。"));

        Widgets.OpenForm(this, "把来源金额归属到控制项", "分配累计不得超过来源金额。", body, "分配", async () =>
        {
            if (fact.SelectedItem is not Pick<int> f) return "请选择来源金额事实。";
            if (item.SelectedItem is not Pick<int> it) return "请选择归属控制项。";
            if (precision.SelectedItem is not Pick<ManagementPrecision> pr) return "请选择分配精度。";
            if (status.SelectedItem is not Pick<AllocationStatus> st) return "请选择分配状态。";
            try
            {
                await _svc.AllocateAsync(f.Value, it.Value, amount.Value ?? 0m, pr.Value, st.Value, note.Text);
                await RefreshAsync();
                SetMsg($"已分配 {UiText.Money(amount.Value ?? 0m)} 到 {it.Label}。");
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
