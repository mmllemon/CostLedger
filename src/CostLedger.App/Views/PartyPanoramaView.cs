using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
/// 往来单位全景（新口径）：选一个单位 → 看清它在公司全部项目上的合同、支出与资金敞口。
/// 未付/未收带「（推算）」，角色为「项目+板块」属性，不跨板块合并成净额；打印带口径声明。
/// </summary>
public class PartyPanoramaView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly PartyPanoramaService _svc = null!;
    private PartyPanorama? _current;

    private ComboBox _partyCombo = null!;
    private WrapPanel _cardHost = null!;
    private StackPanel _body = null!;
    private TextBlock _title = null!;

    public PartyPanoramaView(AppDbContext db)
    {
        _db = db;
        _svc = new PartyPanoramaService(db);
        Content = BuildUi();
        _ = InitAsync();
    }

    private Control BuildUi()
    {
        _partyCombo = new ComboBox { Width = 340, PlaceholderText = "选择往来单位" };
        _partyCombo.DisplayMemberBinding = new Binding("Name");
        _partyCombo.SelectionChanged += async (_, _) => { if (_partyCombo.SelectedItem is Party p) { _current = await _svc.BuildAsync(p.Id); Render(); } };

        _title = new TextBlock { FontSize = 12, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var print = Widgets.ActionButton("打印单位汇报", () => OnPrint(this, new Avalonia.Interactivity.RoutedEventArgs()));

        var top = Widgets.PageHeaderWithDynamicSubtitle("往来", "往来单位全景",
            "看清单位在全部项目上的合同、支出与资金敞口", _title,
            print);

        var selectBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        selectBar.Children.Add(new TextBlock { Text = "选择单位", VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Txt2, FontSize = 12.5 });
        selectBar.Children.Add(_partyCombo);

        _cardHost = new WrapPanel();
        _body = new StackPanel { Spacing = 14 };

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(Widgets.Card(null, selectBar));
        root.Children.Add(Widgets.Card(null, _cardHost));
        root.Children.Add(_body);
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task InitAsync()
    {
        var parties = await _svc.ListPartiesAsync();
        _partyCombo.ItemsSource = parties;
        if (parties.Count > 0) _partyCombo.SelectedIndex = 0;
    }

    private void Render()
    {
        var l = _current;
        if (l is null) return;
        _title.Text = $"{l.PartyName}（{l.ProjectCount} 个项目 · {l.ContractCount} 份合同）";

        _cardHost.Children.Clear();
        void Card(string label, string value, bool bad = false) => _cardHost.Children.Add(Widgets.Kpi(label, value, bad, 190));
        Card("合同总额", UiText.Money(l.ContractTotal));
        Card("对上合同额", UiText.Money(l.UpstreamAmount));
        Card("对下合同额", UiText.Money(l.DownstreamAmount));
        Card("累计付款", UiText.Money(l.Paid));
        Card("未付（推算）", UiText.Money(l.Unpaid), l.Unpaid > 0);
        Card("累计收款", UiText.Money(l.Received));
        Card("未收（推算）", UiText.Money(l.Unreceived), l.Unreceived > 0);
        Card("涉及项目 / 合同", $"{l.ProjectCount} / {l.ContractCount}");

        _body.Children.Clear();
        _body.Children.Add(Widgets.Card("按项目（角色来自「项目 + 业务板块」关系，不跨板块合并）", BuildProjectTable(l)));
        _body.Children.Add(Widgets.Card("合同明细", BuildContractTable(l)));
        _body.Children.Add(Widgets.Card(null, new TextBlock
        {
            Text = PartyLedgerCaliber.Build(UiText.Date(l.AsOfDate)),
            FontSize = 11, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap, LineHeight = 19
        }));
    }

    private static Control BuildProjectTable(PartyPanorama l)
    {
        var headers = new[] { "项目", "角色（板块）", "合同额", "累计付款", "未付（推算）", "累计收款", "截至" };
        var right = new HashSet<int> { 2, 3, 4, 5 };
        var rows = l.ByProject.Select(r => new[]
        {
            r.ProjectName, r.Roles, UiText.Money(r.ContractAmount), UiText.Money(r.Paid),
            UiText.Money(r.Unpaid), UiText.Money(r.Received), UiText.Date(r.AsOfDate)
        });
        return Widgets.Table(headers, rows, right, r => Parse(r[4]) > 0);
    }

    private static Control BuildContractTable(PartyPanorama l)
    {
        var headers = new[] { "项目", "合同编号", "类型（板块）", "合同额", "已付", "未付（推算）" };
        var right = new HashSet<int> { 3, 4, 5 };
        var rows = l.Contracts.Select(r => new[]
        {
            r.ProjectName, r.ContractCode, r.TypeCode, UiText.Money(r.Amount), UiText.Money(r.Paid), UiText.Money(r.Unpaid)
        });
        return Widgets.Table(headers, rows, right, r => Parse(r[5]) > 0);
    }

    private static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var v) ? v : 0m;

    private async void OnPrint(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_current is null) return;
        var l = _current;
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
        var ledger = new LedgerResult($"往来单位资金敞口汇报表 · {l.PartyName}", headers, rows, footers);
        var company = await new SystemParamService(_db).GetAsync("公司名称");
        var caliber = PartyLedgerCaliber.Build(UiText.Date(l.AsOfDate));
        PrintService.OpenInBrowser(PrintService.BuildHtml(l.PartyName, ledger, DateTime.Now, company, caliber), "往来单位台账");
    }
}
