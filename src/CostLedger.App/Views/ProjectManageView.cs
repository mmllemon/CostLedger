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
            Widgets.ActionButton("初始化控制项模板", () => OpenControlItemTemplate()),
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

    /// <summary>
    /// 为项目一次性建立可编辑的成本控制项起始模板。
    /// 仅补充缺失编码，不覆盖项目已有控制项；模板是过程管理口径，不是清单或财务科目。
    /// </summary>
    private void OpenControlItemTemplate()
    {
        var projects = _db.Projects.AsNoTracking().OrderBy(p => p.Code).ToList();
        if (projects.Count == 0)
        {
            SetMsg("请先新建项目，再初始化成本控制项。", true);
            return;
        }

        var projectPicker = new ComboBox
        {
            Width = 400,
            ItemsSource = projects,
            DisplayMemberBinding = new Binding("Name"),
            SelectedIndex = 0
        };

        var template = new (string Code, string Name)[]
        {
            ("LAB-01", "劳务人工及班组"),
            ("MAT-01", "钢材及金属材料"),
            ("MAT-02", "电缆及电气材料"),
            ("MAT-03", "管材、管件及阀件"),
            ("MAT-04", "设备及主要材料"),
            ("MAT-05", "辅材及安装耗材"),
            ("SUB-01", "土建专业分包"),
            ("SUB-02", "安装专业分包"),
            ("SUB-03", "其他专业分包"),
            ("MCH-01", "机械租赁及机具"),
            ("TRN-01", "运输、装卸及二次搬运"),
            ("TST-01", "试验、调试及检测"),
            ("SRV-01", "设计、咨询及技术服务"),
            ("SRV-02", "测量及第三方服务"),
            ("TMP-01", "临时设施及场地"),
            ("TMP-02", "临时水电及能源"),
            ("MGT-01", "项目管理人员及办公"),
            ("MGT-02", "安全文明施工措施"),
            ("MGT-03", "现场措施及零星费用"),
            ("OTH-01", "其他待识别成本")
        };

        var preview = new StackPanel { Spacing = 4 };
        preview.Children.Add(new TextBlock
        {
            Text = "将检查并补齐 " + template.Length + " 个常用控制项（已存在的编码会跳过）",
            FontSize = 12.5,
            Foreground = Palette.Txt2,
            TextWrapping = TextWrapping.Wrap
        });
        preview.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", template.Select(x => x.Name)),
            FontSize = 11.5,
            Foreground = Palette.Txt3,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        });

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Widgets.Row("目标项目", projectPicker, 90));
        body.Children.Add(preview);

        Widgets.OpenForm(this, "初始化成本控制项", "模板只提供过程管理的起始分类，可在「成本控制项与归属」中继续调整；不会创建清单项，也不会覆盖现有控制项。", body, "补齐模板", async () =>
        {
            if (projectPicker.SelectedItem is not Project project)
                return "请选择一个项目。";

            try
            {
                var service = new EconomicFactsService(_db);
                var existingCodes = await _db.CostControlItems
                    .Where(x => x.ProjectId == project.Id)
                    .Select(x => x.Code)
                    .ToListAsync();
                var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var created = 0;
                foreach (var item in template)
                {
                    if (existing.Contains(item.Code)) continue;
                    await service.CreateControlItemAsync(project.Id, item.Code, item.Name, created + 1);
                    created++;
                }

                SetMsg(created == 0
                    ? project.Name + " 已包含模板中的全部控制项，未重复创建。"
                    : "已为 " + project.Name + " 补齐 " + created + " 个成本控制项；已有控制项未被修改。可进入「成本控制项与归属」继续调整。");
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });
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
