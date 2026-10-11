using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 项目管理（主数据）：列出全部项目，页头「新建项目」弹层录入。
/// 项目是全公司共享主数据，切项目在顶栏；本页负责建立项目本身。
/// </summary>
public class ProjectManageView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly ProjectService _svc = null!;
    private readonly StackPanel _host = new();
    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Txt2 };
    private readonly TextBlock _subtitle = new() { FontSize = 11.5, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };

    public ProjectManageView(AppDbContext db)
    {
        _db = db;
        _svc = new ProjectService(db);
        Content = BuildUi();
        _ = ReloadAsync();
    }

    private Control BuildUi()
    {
        var top = Widgets.PageHeaderWithDynamicSubtitle("项目", "项目管理",
            "公司共享主数据；项目建成后到各业务页登记金额与预测", _subtitle,
            Widgets.PrimaryActionButton("＋ 新建项目", () => OpenCreate()));

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(_msg);
        root.Children.Add(Widgets.Card("全部项目", _host));
        return new ScrollViewer { Content = root, Background = Palette.Bg };
    }

    private async Task ReloadAsync()
    {
        var projects = await _svc.ListAsync();
        _subtitle.Text = $"共 {projects.Count} 个项目";

        _host.Children.Clear();
        if (projects.Count == 0)
        {
            _host.Children.Add(Widgets.EmptyState("还没有项目，点击右上角「＋ 新建项目」开始"));
            return;
        }

        var headers = new[] { "项目编号", "项目名称", "建设单位", "计划开始", "计划完工" };
        var rows = projects.Select(p => new[]
        {
            p.Code, p.Name, p.OwnerName ?? "—",
            p.StartDate == default ? "—" : p.StartDate.ToString("yyyy-MM-dd"),
            p.PlannedEndDate is DateOnly d ? d.ToString("yyyy-MM-dd") : "—"
        });
        _host.Children.Add(Widgets.Table(headers, rows));
    }

    private void OpenCreate()
    {
        var code = new TextBox { Width = 400, Watermark = "项目编号（必填，唯一）" };
        var name = new TextBox { Width = 400, Watermark = "项目名称（必填）" };
        var owner = new TextBox { Width = 400, Watermark = "建设单位 / 甲方（可空）" };
        var start = new DatePicker { Width = 400, SelectedDate = new DateTimeOffset(DateTime.Today) };
        var end = new DatePicker { Width = 400 };
        var remark = new TextBox { Width = 400, Watermark = "备注（可空）" };

        var body = new StackPanel { Spacing = 10 };
        foreach (var (l, c) in new (string, Control)[]
        {
            ("项目编号", code), ("项目名称", name), ("建设单位", owner),
            ("计划开始", start), ("计划完工", end), ("备注", remark)
        })
            body.Children.Add(Widgets.Row(l, c, 90));

        Widgets.OpenForm(this, "新建项目", "编号与名称必填，编号全公司唯一。", body, "新建", async () =>
        {
            var s = start.SelectedDate is DateTimeOffset ds ? DateOnly.FromDateTime(ds.DateTime) : DateOnly.FromDateTime(DateTime.Today);
            DateOnly? e = end.SelectedDate is DateTimeOffset de ? DateOnly.FromDateTime(de.DateTime) : null;
            try
            {
                var p = await _svc.CreateProjectAsync(code.Text ?? "", name.Text ?? "", owner.Text, s, e, remark.Text);
                await ReloadAsync();
                SetMsg($"已新建项目 {p.Code} {p.Name}。可在顶部「当前项目」切换器中选择它。");
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
