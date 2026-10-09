using Avalonia;
using Avalonia.Controls;
using CostLedger.App.ViewModels;
using CostLedger.App.Views;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Linq;

namespace CostLedger.App;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db = null!;
    private readonly ICostSpineService _spine = null!;
    private readonly MetadataService _metadata = null!;
    private readonly ReportService _reports = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ICostSpineService spine, AppDbContext db, MetadataService metadata)
    {
        _db = db;
        _spine = spine;
        _metadata = metadata;
        _reports = new ReportService(db);
        InitializeComponent();
        Closed += (_, _) => _db.Dispose();
        NavigateTo("dashboard");
    }

    private void OnNavClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = (Button?)sender;
        if (btn is null) return;
        NavigateTo(btn.Name ?? "");
    }

    private void NavigateTo(string key)
    {
        foreach (var b in new[] { NavDashboard, NavMaster, NavProject, NavCategory, NavReports, NavSettings })
            b.Classes.Remove("on");

        switch (key)
        {
            case "NavMaster":
                NavMaster.Classes.Add("on");
                MainArea.Content = new MasterDataView(_db, _metadata);
                break;
            case "NavProject":
                NavProject.Classes.Add("on");
                MainArea.Content = new GenericRecordView(_db, _metadata, "Project", typeof(Project),
                    new[]
                    {
                        new CoreFieldDescriptor("项目编号", "Code", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("项目名称", "Name", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("建设单位", "OwnerName", CoreFieldKind.Text),
                    },
                    () => new Project(),
                    m => { var p = (Project)m; return $"{p.Code} · {p.Name}"; },
                    db => db.Projects.Cast<object>());
                break;
            case "NavCategory":
                NavCategory.Classes.Add("on");
                MainArea.Content = new GenericRecordView(_db, _metadata, "CostCategory", typeof(CostCategory),
                    new[]
                    {
                        new CoreFieldDescriptor("科目编码", "Code", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("科目名称", "Name", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("类型", "Type", CoreFieldKind.Enum, EnumType: typeof(CostCategoryType)),
                    },
                    () => new CostCategory(),
                    m => { var c = (CostCategory)m; return $"{c.Code} · {c.Name}"; },
                    db => db.CostCategories.Cast<object>());
                break;
            case "NavReports":
                NavReports.Classes.Add("on");
                MainArea.Content = new ReportCenterView(_reports);
                break;
            case "NavSettings":
                NavSettings.Classes.Add("on");
                MainArea.Content = new SystemSettingsView(_metadata);
                break;
            default:
                NavDashboard.Classes.Add("on");
                var vm = new DashboardViewModel(_spine);
                var dash = new DashboardView { DataContext = vm };
                MainArea.Content = dash;
                _ = vm.LoadAsync();
                break;
        }
    }
}
