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
/// 成本控制项与归属：建控制项 → 登记业务关系 → 登记外部金额事实 → 分配到控制项。
/// 全部通过 EconomicFactsService，领域层的板块/角色/方向规则在这里只做展示与透传，
/// 违反规则时把服务返回的中文纠正文案直接显示，不再自行放宽口径。
/// </summary>
public class CostControlView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly EconomicFactsService _svc = null!;
    private Project? _current;

    private ComboBox _projCombo = null!;
    private StackPanel _itemHost = null!;
    private ComboBox _factCombo = null!;
    private TextBlock _msg = null!;

    // 新建控制项
    private TextBox _newCode = null!, _newName = null!, _newSort = null!;
    // 业务关系
    private ComboBox _relParty = null!, _relSegment = null!, _relDirection = null!, _relRole = null!;
    private TextBox _relContract = null!, _relNote = null!;
    // 金额事实
    private ComboBox _factSegment = null!, _factDirection = null!, _factParty = null!, _factStatus = null!, _factPrecision = null!;
    private NumericUpDown _factAmount = null!;
    private TextBox _factSource = null!, _factBy = null!, _factDesc = null!;
    private DatePicker _factAsOf = null!;
    // 分配
    private ComboBox _allocItem = null!, _allocPrecision = null!, _allocStatus = null!;
    private NumericUpDown _allocAmount = null!;
    private TextBox _allocNote = null!;

    private IReadOnlyList<Pick<BusinessSegment>> _segments = UiText.Segments();
    private IReadOnlyList<Pick<EconomicDirection>> _directions = UiText.Directions();
    private IReadOnlyList<Pick<ExternalFactStatus>> _statuses = UiText.Statuses();
    private IReadOnlyList<Pick<ManagementPrecision>> _precisions = UiText.Precisions();

    public CostControlView(AppDbContext db)
    {
        _db = db;
        _svc = new EconomicFactsService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _projCombo = new ComboBox { Width = 300, PlaceholderText = "选择项目" };
        _projCombo.DisplayMemberBinding = new Binding("Name");
        _projCombo.SelectionChanged += async (_, _) => { if (_projCombo.SelectedItem is Project p) { _current = p; await RefreshAsync(); } };

        _msg = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
        _itemHost = new StackPanel();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(new TextBlock { Text = "项目", VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Txt2 });
        top.Children.Add(_projCombo);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        root.Children.Add(top);
        root.Children.Add(Widgets.Card("成本控制项（过程管理口径，不要求与合同清单逐项对齐）", BuildItemSection()));
        root.Children.Add(BuildRelationshipCard());
        root.Children.Add(BuildFactCard());
        root.Children.Add(BuildAllocateCard());
        root.Children.Add(_msg);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private Control BuildItemSection()
    {
        _newCode = new TextBox { Watermark = "控制项编号", Width = 110 };
        _newName = new TextBox { Watermark = "控制项名称", Width = 200 };
        _newSort = new TextBox { Watermark = "排序", Width = 70, Text = "0" };
        var add = new Button { Content = "新建控制项" };
        add.Classes.Add("primary");
        add.Click += OnAddItem;

        var form = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 10) };
        form.Children.Add(_newCode); form.Children.Add(_newName); form.Children.Add(_newSort); form.Children.Add(add);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(form);
        stack.Children.Add(new ScrollViewer { Content = _itemHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 });
        return stack;
    }

    private Control BuildRelationshipCard()
    {
        _relParty = new ComboBox { Width = 240, PlaceholderText = "对方单位" };
        _relParty.DisplayMemberBinding = new Binding("Name");
        _relSegment = new ComboBox { ItemsSource = _segments, Width = 150, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块" };
        _relDirection = new ComboBox { ItemsSource = _directions, Width = 110, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "金额方向" };
        _relRole = new ComboBox { Width = 190, PlaceholderText = "角色（字典）" };
        _relContract = new TextBox { Watermark = "合同编号（可空）", Width = 160 };
        _relNote = new TextBox { Watermark = "备注（可空）", Width = 200 };

        var submit = new Button { Content = "登记业务关系" };
        submit.Classes.Add("primary");
        submit.Click += OnRegisterRelationship;

        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(Widgets.Row("对方单位", _relParty));
        rows.Children.Add(Widgets.Row("角色", _relRole));
        rows.Children.Add(Widgets.Row("业务板块", _relSegment));
        rows.Children.Add(Widgets.Row("金额方向", _relDirection));
        rows.Children.Add(Widgets.Row("合同编号", _relContract));
        rows.Children.Add(Widgets.Row("备注", _relNote));
        rows.Children.Add(submit);
        rows.Children.Add(Widgets.Hint("方向由板块决定：资金状态只允许已收/已付；对上收入只允许收入/中性；材料·劳务·分包·服务·间接费只允许支出/中性。角色须在字典「项目业务角色」中启用。"));
        return Widgets.Card("登记业务关系（同一单位可在一个项目里承担多个板块角色）", rows);
    }

    private Control BuildFactCard()
    {
        _factSegment = new ComboBox { ItemsSource = _segments, Width = 150, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块" };
        _factDirection = new ComboBox { ItemsSource = _directions, Width = 110, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "金额方向" };
        _factAmount = new NumericUpDown { Width = 160, FormatString = "N2", Increment = 1000 };
        _factParty = new ComboBox { Width = 240, PlaceholderText = "对方单位（可空）" };
        _factParty.DisplayMemberBinding = new Binding("Name");
        _factSource = new TextBox { Watermark = "来源（必填，如合同号/台账/日报）", Width = 260 };
        _factBy = new TextBox { Watermark = "提供方（可空）", Width = 160 };
        _factAsOf = new DatePicker { Width = 160 };
        _factStatus = new ComboBox { ItemsSource = _statuses, Width = 130, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "数据状态" };
        _factPrecision = new ComboBox { ItemsSource = _precisions, Width = 130, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "管理精度" };
        _factDesc = new TextBox { Watermark = "说明（可空）", Width = 260 };

        var submit = new Button { Content = "登记外部金额事实" };
        submit.Classes.Add("primary");
        submit.Click += OnRegisterFact;

        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(Widgets.Row("业务板块", _factSegment));
        rows.Children.Add(Widgets.Row("金额方向", _factDirection));
        rows.Children.Add(Widgets.Row("金额", _factAmount));
        rows.Children.Add(Widgets.Row("对方单位", _factParty));
        rows.Children.Add(Widgets.Row("来源", _factSource));
        rows.Children.Add(Widgets.Row("提供方", _factBy));
        rows.Children.Add(Widgets.Row("数据截至", _factAsOf));
        rows.Children.Add(Widgets.Row("数据状态", _factStatus));
        rows.Children.Add(Widgets.Row("管理精度", _factPrecision));
        rows.Children.Add(Widgets.Row("说明", _factDesc));
        rows.Children.Add(submit);
        rows.Children.Add(Widgets.Hint("系统只记录你获知的金额、来源与截至日期，不判定财务是否入账。挂对方单位时，方向沿用该单位在此板块已建立的业务关系；未建关系会被拒绝。"));
        return Widgets.Card("登记外部金额事实", rows);
    }

    private Control BuildAllocateCard()
    {
        _factCombo = new ComboBox { Width = 420, PlaceholderText = "选择来源金额事实" };
        _factCombo.DisplayMemberBinding = new Binding("Label");
        _allocItem = new ComboBox { Width = 240, PlaceholderText = "归属控制项" };
        _allocItem.DisplayMemberBinding = new Binding("Label");
        _allocAmount = new NumericUpDown { Width = 160, FormatString = "N2", Increment = 1000 };
        _allocPrecision = new ComboBox { ItemsSource = _precisions, Width = 130, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "分配精度" };
        _allocStatus = new ComboBox { ItemsSource = UiText.AllocationStatuses(), Width = 110, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "状态" };
        _allocNote = new TextBox { Watermark = "备注（可空）", Width = 220 };

        var submit = new Button { Content = "分配到控制项" };
        submit.Classes.Add("primary");
        submit.Click += OnAllocate;

        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(Widgets.Row("来源金额事实", _factCombo, 96));
        rows.Children.Add(Widgets.Row("归属控制项", _allocItem, 96));
        rows.Children.Add(Widgets.Row("分配金额", _allocAmount, 96));
        rows.Children.Add(Widgets.Row("分配精度", _allocPrecision, 96));
        rows.Children.Add(Widgets.Row("状态", _allocStatus, 96));
        rows.Children.Add(Widgets.Row("备注", _allocNote, 96));
        rows.Children.Add(submit);
        rows.Children.Add(Widgets.Hint("允许一笔来源拆到多个控制项、允许暂估与不分配；分配累计不得超过来源金额（超限直接拒绝）。"));
        return Widgets.Card("把来源金额归属到控制项", rows);
    }

    /// <summary>由顶部全局项目切换器驱动。</summary>
    public void SelectProject(Project project)
    {
        _current = project;
        if (_projCombo.ItemsSource != null) _projCombo.SelectedItem = project;
        _ = RefreshAsync();
    }

    private async Task InitAsync()
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        _projCombo.ItemsSource = projects;
        var parties = await _db.Parties.OrderBy(p => p.Name).ToListAsync();
        _relParty.ItemsSource = parties;
        _factParty.ItemsSource = parties;
        if (projects.Count > 0) _projCombo.SelectedIndex = 0;
    }

    private async Task RefreshAsync()
    {
        if (_current is null) return;
        var items = await _db.CostControlItems.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync();
        _allocItem.ItemsSource = items.Select(i => new Pick<int>(i.Id, $"{i.Code} · {i.Name}")).ToList();

        var facts = await _db.ExternalAmountFacts.AsNoTracking()
            .Where(x => x.ProjectId == _current.Id).OrderByDescending(x => x.AsOfDate).ToListAsync();
        _factCombo.ItemsSource = facts.Select(x => new Pick<int>(x.Id,
            $"{UiText.Segment(x.Segment)} · {UiText.Money(x.Amount)} · {x.Source}")).ToList();

        var roles = await _svc.ListRoleCodesAsync();
        _relRole.ItemsSource = roles;

        var summary = await _svc.SummarizeControlItemsAsync(_current.Id);
        RenderItems(items, summary);
        _msg.Text = summary.Count == 0 ? "该项目还没有已归属金额。" : "";
    }

    private void RenderItems(List<CostControlItem> items, List<CostControlItemSummary> summary)
    {
        _itemHost.Children.Clear();
        var byId = summary.ToDictionary(x => x.CostControlItemId);
        var headers = new[] { "编号", "控制项", "已分配", "暂估", "已核对", "最粗来源精度", "来源数" };
        var right = new HashSet<int> { 2, 3, 4, 6 };
        var rows = items.Select(i =>
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

    private async void OnAddItem(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        var code = _newCode.Text?.Trim() ?? "";
        var name = _newName.Text?.Trim() ?? "";
        if (code.Length == 0 || name.Length == 0) { SetMsg("控制项编号与名称必填。", true); return; }
        if (await _db.CostControlItems.AnyAsync(x => x.ProjectId == _current.Id && x.Code == code))
        { SetMsg($"控制项编号 {code} 在本项目已存在。", true); return; }
        int.TryParse(_newSort.Text, out var sort);
        _db.CostControlItems.Add(new CostControlItem { ProjectId = _current.Id, Code = code, Name = name, SortOrder = sort, IsActive = true });
        await _db.SaveChangesAsync();
        _newCode.Text = _newName.Text = "";
        SetMsg($"已新建控制项 {code} {name}。");
        await RefreshAsync();
    }

    private async void OnRegisterRelationship(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_relParty.SelectedItem is not Party party) { SetMsg("请选择对方单位。", true); return; }
        if (_relSegment.SelectedItem is not Pick<BusinessSegment> seg) { SetMsg("请选择业务板块。", true); return; }
        if (_relDirection.SelectedItem is not Pick<EconomicDirection> dir) { SetMsg("请选择金额方向。", true); return; }
        if (_relRole.SelectedItem is not string role || role.Length == 0) { SetMsg("请选择角色。", true); return; }

        int? contractId = null;
        var ccode = _relContract.Text?.Trim();
        if (!string.IsNullOrEmpty(ccode))
        {
            var c = await _db.Contracts.FirstOrDefaultAsync(x => x.ProjectId == _current.Id && x.Code == ccode);
            if (c is null) { SetMsg($"本项目未找到合同编号 {ccode}。", true); return; }
            contractId = c.Id;
        }

        try
        {
            await _svc.RegisterRelationshipAsync(new ProjectPartyRelationship
            {
                ProjectId = _current.Id, PartyId = party.Id, ContractId = contractId,
                RoleCode = role, Segment = seg.Value, Direction = dir.Value, Note = _relNote.Text
            });
            SetMsg($"已登记：{party.Name} 在「{UiText.Segment(seg.Value)}」承担 {role}（{UiText.Direction(dir.Value)}）。");
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private async void OnRegisterFact(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_factSegment.SelectedItem is not Pick<BusinessSegment> seg) { SetMsg("请选择业务板块。", true); return; }
        if (_factDirection.SelectedItem is not Pick<EconomicDirection> dir) { SetMsg("请选择金额方向。", true); return; }
        if (_factStatus.SelectedItem is not Pick<ExternalFactStatus> st) { SetMsg("请选择数据状态。", true); return; }
        if (_factPrecision.SelectedItem is not Pick<ManagementPrecision> pr) { SetMsg("请选择管理精度。", true); return; }
        if (string.IsNullOrWhiteSpace(_factSource.Text)) { SetMsg("外部金额事实必须填写来源。", true); return; }

        var asOf = _factAsOf.SelectedDate is DateTimeOffset d ? DateOnly.FromDateTime(d.DateTime) : DateOnly.FromDateTime(DateTime.Today);
        var fact = new ExternalAmountFact
        {
            ProjectId = _current.Id, Segment = seg.Value, Direction = dir.Value,
            Amount = _factAmount.Value ?? 0m,
            PartyId = (_factParty.SelectedItem as Party)?.Id,
            Source = _factSource.Text!.Trim(), ProvidedBy = string.IsNullOrWhiteSpace(_factBy.Text) ? null : _factBy.Text!.Trim(),
            AsOfDate = asOf, Status = st.Value, Precision = pr.Value,
            Description = string.IsNullOrWhiteSpace(_factDesc.Text) ? null : _factDesc.Text!.Trim()
        };
        try
        {
            await _svc.RegisterFactAsync(fact);
            SetMsg($"已登记外部金额事实 {UiText.Money(fact.Amount)}（{UiText.Segment(fact.Segment)}）。");
            _factAmount.Value = 0; _factSource.Text = ""; _factDesc.Text = "";
            await RefreshAsync();
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private async void OnAllocate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_factCombo.SelectedItem is not Pick<int> fact) { SetMsg("请选择来源金额事实。", true); return; }
        if (_allocItem.SelectedItem is not Pick<int> item) { SetMsg("请选择归属控制项。", true); return; }
        if (_allocPrecision.SelectedItem is not Pick<ManagementPrecision> pr) { SetMsg("请选择分配精度。", true); return; }
        if (_allocStatus.SelectedItem is not Pick<AllocationStatus> st) { SetMsg("请选择分配状态。", true); return; }
        try
        {
            await _svc.AllocateAsync(fact.Value, item.Value, _allocAmount.Value ?? 0m, pr.Value, st.Value, _allocNote.Text);
            SetMsg($"已分配 {UiText.Money(_allocAmount.Value ?? 0m)} 到 {item.Label}。");
            _allocAmount.Value = 0; _allocNote.Text = "";
            await RefreshAsync();
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private void SetMsg(string text, bool error = false)
    {
        _msg.Text = text;
        _msg.Foreground = error ? Palette.Red : Palette.Txt2;
    }
}
