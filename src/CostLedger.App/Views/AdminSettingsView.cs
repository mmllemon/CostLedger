using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 后台管理：把可增补项（角色字典 / 板块角色授权 / 字典 / 字段 / 参数 / 账套）集中一处。
/// 口径骨架（板块、金额方向、精度、数据状态）在此只读展示，不提供新增或放宽入口。
/// </summary>
public class AdminSettingsView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly MetadataService _metadata = null!;
    private readonly EconomicFactsService _facts = null!;
    private readonly StackPanel _roleList = new();
    private readonly StackPanel _grantList = new();
    private readonly StackPanel _skeletonList = new();

    public AdminSettingsView(AppDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
        _facts = new EconomicFactsService(db);
        Content = BuildUi();
        _ = LoadAsync();
    }

    private Control BuildUi()
    {
        var title = new TextBlock { Text = "后台管理", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Palette.Ink };

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(title);
        root.Children.Add(Widgets.Card(null, new TextBlock
        {
            Text = "这里集中「可增补项」：项目业务角色、板块角色授权、字典、字段、系统参数与账套。业务板块、金额方向、管理精度、数据状态属于口径骨架，是强类型，此页不提供新增或放宽入口。",
            FontSize = 12, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap, LineHeight = 19
        }));
        root.Children.Add(Widgets.Card("项目业务角色（字典 ProjectPartyRole）", _roleList));
        root.Children.Add(Widgets.Card("板块角色授权（只增补角色，不放宽方向）", _grantList));
        root.Children.Add(Widgets.Card("口径骨架（只读）", _skeletonList));
        root.Children.Add(Widgets.Card("账套与迁移", new TextBlock
        {
            Text = $"当前账套结构版本 SchemaVersion = {DbBootstrap.SchemaVersion}。不兼容版本的账套会被拒绝打开；旧账套迁移路径尚未实现。",
            FontSize = 12.5, Foreground = Palette.Txt2, TextWrapping = TextWrapping.Wrap
        }));
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task LoadAsync()
    {
        _roleList.Children.Clear();
        var roles = await _facts.ListRoleCodesAsync();
        var codeToLabel = new System.Collections.Generic.Dictionary<string, string>
        {
            [PartyRoleCodes.Owner] = "建设单位",
            [PartyRoleCodes.MaterialSupplier] = "材料供应单位",
            [PartyRoleCodes.LaborSubcontractor] = "劳务分包单位",
            [PartyRoleCodes.SpecialistSubcontractor] = "专业分包单位",
            [PartyRoleCodes.ProfessionalServiceProvider] = "专业服务单位",
            [PartyRoleCodes.InternalProvider] = "内部提供单位"
        };
        var roleChips = new WrapPanel();
        foreach (var r in roles)
            roleChips.Children.Add(Widgets.Badge(codeToLabel.TryGetValue(r, out var l) ? $"{l}（{r}）" : r, "accent"));
        _roleList.Children.Add(roleChips);
        _roleList.Children.Add(Widgets.Hint("角色由字典驱动：在此之外新增的角色，需先进入「项目业务角色」字典，再到下方授权到具体板块。"));

        _grantList.Children.Clear();
        var grants = await _facts.ListSegmentRolePermissionsAsync();
        if (grants.Count == 0)
            _grantList.Children.Add(new TextBlock { Text = "尚无板块角色授权。", FontSize = 12.5, Foreground = Palette.Txt3 });
        else
        {
            var headers = new[] { "业务板块", "角色代码", "备注" };
            var rows = new System.Collections.Generic.List<string[]>();
            foreach (var g in grants)
                rows.Add(new[] { UiText.Segment(g.Segment), g.RoleCode, g.Note ?? "—" });
            _grantList.Children.Add(Widgets.Table(headers, rows));
        }

        _skeletonList.Children.Clear();
        var sk = new[]
        {
            new[] { "业务板块", string.Join(" / ", System.Linq.Enumerable.Select(System.Enum.GetValues<BusinessSegment>(), UiText.Segment)), "决定金额方向与口径" },
            new[] { "金额方向", "收入 / 支出 / 已收 / 已付 / 中性", "由板块决定，不由人填" },
            new[] { "管理精度", "项目级 / 专业级 / 合同包级 / 控制项级 / 清单级", "精度封顶防止粗数据伪装" },
            new[] { "数据状态", "估计 / 外部提供 / 已核对 / 已过期 / 待确认", "状态语义固定" },
        };
        _skeletonList.Children.Add(Widgets.Table(new[] { "骨架项", "取值", "为什么不能配置" }, sk));
    }
}
