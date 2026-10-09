using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private readonly DashboardService _dashboard = null!;
    private readonly AlertService _alerts = null!;
    private readonly SchedulerService _scheduler = null!;

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
        _dashboard = new DashboardService(db, spine);
        _alerts = new AlertService(db, _dashboard);
        _scheduler = new SchedulerService(db, _alerts);
        InitializeComponent();
        Closed += (_, _) => _db.Dispose();
        StartScheduler();
        NavigateTo("dashboard");
    }

    /// <summary>每 60 秒检查一次到期计划任务（预警评估等）；失败不阻塞 UI。</summary>
    private void StartScheduler()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += async (_, _) =>
        {
            try { await _scheduler.RunDueAsync(DateTime.UtcNow); }
            catch (Exception ex) { Console.WriteLine($"[scheduler] {ex.Message}"); }
        };
        timer.Start();
    }

    private void OnNavClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = (Button?)sender;
        if (btn is null) return;
        NavigateTo(btn.Name ?? "");
    }

    private void NavigateTo(string key)
    {
        foreach (var b in new[] { NavDashboard, NavOverview, NavLedgers, NavParties, NavCost, NavMaster, NavDocs, NavProject, NavCategory, NavReports, NavBoardDesign, NavAlerts, NavRecon, NavSettings })
            b.Classes.Remove("on");

        switch (key)
        {
            case "NavOverview":
                NavOverview.Classes.Add("on");
                MainArea.Content = new ProjectOverviewView(_db, _alerts);
                break;
            case "NavLedgers":
                NavLedgers.Classes.Add("on");
                MainArea.Content = new LedgerCenterView(_db);
                break;
            case "NavParties":
                NavParties.Classes.Add("on");
                MainArea.Content = new PartyLedgerView(_db);
                break;
            case "NavCost":
                NavCost.Classes.Add("on");
                MainArea.Content = new CostStructureView(_db);
                break;
            case "NavMaster":
                NavMaster.Classes.Add("on");
                MainArea.Content = new MasterDataView(_db, _metadata);
                break;
            case "NavDocs":
                NavDocs.Classes.Add("on");
                MainArea.Content = new BusinessDocsView(_db);
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
            case "NavBoardDesign":
                NavBoardDesign.Classes.Add("on");
                MainArea.Content = new DashboardSettingsView(_dashboard);
                break;
            case "NavAlerts":
                NavAlerts.Classes.Add("on");
                MainArea.Content = new AlertCenterView(_alerts, _scheduler);
                break;
            case "NavRecon":
                NavRecon.Classes.Add("on");
                MainArea.Content = new ReconciliationView(_db);
                break;
            case "NavSettings":
                NavSettings.Classes.Add("on");
                MainArea.Content = new SystemSettingsView(_metadata, _db);
                break;
            default:
                NavDashboard.Classes.Add("on");
                var vm = new DashboardViewModel(_spine, _dashboard);
                var dash = new DashboardView { DataContext = vm };
                MainArea.Content = dash;
                _ = vm.LoadAsync();
                break;
        }
    }
}
