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
/// 板块金额登记（独立页，对应预览稿）：先定业务板块 → 角色决定于"项目+板块"关系 →
/// 方向由板块决定。枚举用分段按钮，金额方向只读展示。
/// 同时提供"登记业务关系"（弹层），因为角色须先建立关系才能登记金额。
/// </summary>
public class RegisterAmountView : UserControl, IProjectScoped
{
    private readonly AppDbContext _db = null!;
    private readonly EconomicFactsService _svc = null!;
    private Project? _current;

    private readonly TextBlock _title = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
    private readonly StackPanel _segHost = new();
    private readonly TextBlock _directionText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _partyHint = new() { FontSize = 11, Foreground = Palette.Txt3, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };

    private readonly ComboBox _party = new() { Width = 320, PlaceholderText = "对方单位（选择后方向沿用其关系）" };
    private readonly NumericUpDown _amount = new() { Width = 320, FormatString = "N2", Increment = 1000 };
    private readonly TextBox _source = new() { Width = 320, Watermark = "来源（必填，如合同号/台账/日报）" };
    private readonly TextBox _by = new() { Width = 320, Watermark = "提供方（可空）" };
    private readonly DatePicker _asOf = new() { Width = 320, SelectedDate = new DateTimeOffset(DateTime.Today) };
    private readonly TextBox _desc = new() { Width = 320, Watermark = "说明（可空）" };

    private BusinessSegment _segment = BusinessSegment.MaterialSupply;
    private EconomicDirection _direction = EconomicDirection.Expenditure;
    private ExternalFactStatus _status = ExternalFactStatus.ExternallyProvided;
    private ManagementPrecision _precision = ManagementPrecision.ContractPackage;

    private List<Party> _parties = new();
    private List<string> _roles = new();

    private readonly IReadOnlyList<Pick<BusinessSegment>> _segments = UiText.Segments();
    private readonly IReadOnlyList<Pick<EconomicDirection>> _directions = UiText.Directions();
    private readonly IReadOnlyList<Pick<ExternalFactStatus>> _statuses = UiText.Statuses();
    private readonly IReadOnlyList<Pick<ManagementPrecision>> _precisions = UiText.Precisions();

    public RegisterAmountView(AppDbContext db)
    {
        _db = db;
        _svc = new EconomicFactsService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        var top = Widgets.PageHeaderWithDynamicSubtitle("登记", "板块金额登记",
            "先定业务板块 → 认定角色 → 方向由板块决定", _title,
            Widgets.ActionButton("登记业务关系", () => OpenRelationship()));

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(Widgets.FormField("业务板块", _segHost));
        body.Children.Add(Widgets.FormField("对方单位", PartyBlock()));
        body.Children.Add(Widgets.FormField("金额方向", DirectionBlock()));
        body.Children.Add(Widgets.FormField("金额", _amount));
        body.Children.Add(Widgets.FormField("来源", _source));
        body.Children.Add(Widgets.FormField("提供方", _by));
        body.Children.Add(Widgets.FormField("数据截至", _asOf));
        body.Children.Add(Widgets.FormField("数据状态", StatusSeg()));
        body.Children.Add(Widgets.FormField("管理精度", PrecisionSeg()));

        var card = new StackPanel { Spacing = 0 };
        card.Children.Add(new TextBlock { Text = "登记外部金额事实", FontSize = 14.5, FontWeight = FontWeight.Bold, Foreground = Palette.Ink, Margin = new Thickness(0, 0, 0, 6) });
        card.Children.Add(body);
        card.Children.Add(Widgets.Hint("系统只记录你获知的金额、它从哪来、截至哪天；财务是否已入账、是否合规，不由成本岗判定。"));

        var submit = Widgets.PrimaryActionButton("登记金额", () => _ = SubmitAsync());
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
        actionRow.Children.Add(submit);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card(null, card));
        root.Children.Add(actionRow);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private Control PartyBlock()
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(_party);
        stack.Children.Add(_partyHint);
        return stack;
    }

    private Control DirectionBlock()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_directionText);
        row.Children.Add(Widgets.Badge("由板块决定，不可改", "muted"));
        return row;
    }

    private Control StatusSeg() => Widgets.Segmented(_statuses, _status, v => _status = v);
    private Control PrecisionSeg() => Widgets.Segmented(_precisions, _precision, v => _precision = v);

    private void RebuildSegmentSeg()
    {
        _segHost.Children.Clear();
        _segHost.Children.Add(Widgets.Segmented(_segments, _segment, v =>
        {
            _segment = v;
            _direction = BusinessSegmentRules.AllowedDirections(v).FirstOrDefault();
            if (_direction == default) _direction = EconomicDirection.Expenditure;
            UpdateDirectionText();
        }));
    }

    private void UpdateDirectionText() => _directionText.Text = UiText.Direction(_direction);

    public void SelectProject(Project project)
    {
        _current = project;
        _title.Text = $"{project.Code} · {project.Name}";
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _parties = await _db.Parties.OrderBy(p => p.Name).ToListAsync();
        _party.ItemsSource = _parties;
        _party.DisplayMemberBinding = new Binding("Name");
        _roles = await _svc.ListRoleCodesAsync();

        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        if (_current is null && projects.Count > 0) { _current = projects[0]; _title.Text = $"{_current.Code} · {_current.Name}"; }

        RebuildSegmentSeg();
        UpdateDirectionText();
    }

    private async Task SubmitAsync()
    {
        if (_current is null) { SetMsg("请先选择项目。", true); return; }
        if (string.IsNullOrWhiteSpace(_source.Text)) { SetMsg("外部金额事实必须填写来源。", true); return; }
        var asOf = _asOf.SelectedDate is DateTimeOffset d ? DateOnly.FromDateTime(d.DateTime) : DateOnly.FromDateTime(DateTime.Today);
        try
        {
            await _svc.RegisterFactAsync(new ExternalAmountFact
            {
                ProjectId = _current.Id, Segment = _segment, Direction = _direction,
                Amount = _amount.Value ?? 0m, PartyId = (_party.SelectedItem as Party)?.Id,
                Source = _source.Text!.Trim(), ProvidedBy = string.IsNullOrWhiteSpace(_by.Text) ? null : _by.Text!.Trim(),
                AsOfDate = asOf, Status = _status, Precision = _precision,
                Description = string.IsNullOrWhiteSpace(_desc.Text) ? null : _desc.Text!.Trim()
            });
            SetMsg($"已登记外部金额事实（{UiText.Segment(_segment)} · {UiText.Direction(_direction)}）。");
            _amount.Value = 0; _source.Text = ""; _desc.Text = "";
        }
        catch (Exception ex) { SetMsg(ex.Message, true); }
    }

    private void OpenRelationship()
    {
        if (_current is null) return;
        var party = new ComboBox { ItemsSource = _parties, DisplayMemberBinding = new Binding("Name"), PlaceholderText = "对方单位", Width = 360 };
        var role = new ComboBox { ItemsSource = _roles, PlaceholderText = "角色（字典）", Width = 360 };
        var segment = new ComboBox { ItemsSource = _segments, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "业务板块", Width = 360 };
        var direction = new ComboBox { ItemsSource = _directions, DisplayMemberBinding = new Binding("Label"), PlaceholderText = "金额方向", Width = 360 };
        var contract = new TextBox { Width = 360, Watermark = "合同编号（可空）" };
        var note = new TextBox { Width = 360, Watermark = "备注（可空）" };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("对方单位", party, 90));
        body.Children.Add(Widgets.Row("角色", role, 90));
        body.Children.Add(Widgets.Row("业务板块", segment, 90));
        body.Children.Add(Widgets.Row("金额方向", direction, 90));
        body.Children.Add(Widgets.Row("合同编号", contract, 90));
        body.Children.Add(Widgets.Row("备注", note, 90));

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
                SetMsg($"已登记业务关系：{p.Name} 在「{UiText.Segment(seg.Value)}」承担 {roleCode}。");
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
