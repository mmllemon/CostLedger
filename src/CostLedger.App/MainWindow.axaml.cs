using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using CostLedger.App.ViewModels;
using CostLedger.App.Views;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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
    private readonly DynamicEntityService _dyn = null!;

    private List<Project> _projects = new();
    private Project? _currentProject;
    private Control? _currentView;

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
        _dyn = new DynamicEntityService(db);
        InitializeComponent();
        Closed += (_, _) => _db.Dispose();
        StartScheduler();
        _ = LoadProjectsAsync();
        NavigateTo("NavCompany");
    }

    /// <summary>加载项目并填充顶部全局项目切换器；默认选中第一个项目。</summary>
    private async System.Threading.Tasks.Task LoadProjectsAsync()
    {
        _projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync();
        ProjectSwitcher.ItemsSource = _projects;
        ProjectSwitcher.DisplayMemberBinding = new Binding("Name");
        if (_projects.Count > 0)
        {
            _currentProject = _projects[0];
            ProjectSwitcher.SelectedItem = _currentProject;
        }
    }

    /// <summary>全局项目切换：把选中项目推给当前经济视图（若其支持），并刷新数据截至提示。</summary>
    private async void OnProjectSwitcherChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjectSwitcher.SelectedItem is not Project p) return;
        _currentProject = p;
        var asOf = await _db.ExternalAmountFacts.Where(x => x.ProjectId == p.Id)
            .Select(x => (DateOnly?)x.AsOfDate).MaxAsync();
        AsOfText.Text = asOf is DateOnly d ? $"数据截至 {d:yyyy-MM-dd}" : "尚无外部金额事实";
        if (_currentView is IProjectScoped scoped) scoped.SelectProject(p);
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

    private void SetActive(string key)
    {
        foreach (var b in new[] { NavCompany, NavCockpit, NavCostControl, NavForecast, NavDashboard, NavOverview, NavLedgers, NavParties, NavCost, NavMaster, NavDocs, NavProject, NavCategory, NavReports, NavBoardDesign, NavAlerts, NavRecon, NavSettings, NavAdmin, NavObjects })
            b.Classes.Remove("on");
        var target = key switch
        {
            "NavCompany" => NavCompany,
            "NavCockpit" => NavCockpit,
            "NavCostControl" => NavCostControl,
            "NavForecast" => NavForecast,
            "NavOverview" => NavOverview,
            "NavLedgers" => NavLedgers,
            "NavParties" => NavParties,
            "NavCost" => NavCost,
            "NavMaster" => NavMaster,
            "NavDocs" => NavDocs,
            "NavProject" => NavProject,
            "NavCategory" => NavCategory,
            "NavReports" => NavReports,
            "NavBoardDesign" => NavBoardDesign,
            "NavAlerts" => NavAlerts,
            "NavRecon" => NavRecon,
            "NavSettings" => NavSettings,
            "NavAdmin" => NavAdmin,
            "NavObjects" => NavObjects,
            _ => NavDashboard
        };
        target.Classes.Add("on");
    }

    private void Show(Control view)
    {
        _currentView = view;
        MainArea.Content = view;
        if (view is IProjectScoped && _currentProject != null) ((IProjectScoped)view).SelectProject(_currentProject);
    }

    private void NavigateTo(string key)
    {
        SetActive(key);

        switch (key)
        {
            case "NavCompany":
                _currentView = new CompanyCockpitView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavAdmin":
                _currentView = new AdminSettingsView(_db, _metadata);
                MainArea.Content = _currentView;
                break;
            case "NavCockpit":
                Show(new ProjectCockpitView(_db));
                break;
            case "NavCostControl":
                Show(new CostControlView(_db));
                break;
            case "NavForecast":
                Show(new ForecastView(_db));
                break;
            case "NavOverview":
                MainArea.Content = new ProjectOverviewView(_db, _alerts);
                _currentView = MainArea.Content as Control;
                break;
            case "NavLedgers":
                _currentView = new LedgerCenterView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavParties":
                _currentView = new PartyLedgerView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavCost":
                _currentView = new CostStructureView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavMaster":
                _currentView = new MasterDataView(_db, _metadata);
                MainArea.Content = _currentView;
                break;
            case "NavDocs":
                _currentView = new BusinessDocsView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavProject":
                _currentView = new GenericRecordView(_db, _metadata, "Project", typeof(Project),
                    new[]
                    {
                        new CoreFieldDescriptor("项目编号", "Code", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("项目名称", "Name", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("建设单位", "OwnerName", CoreFieldKind.Text),
                    },
                    () => new Project(),
                    m => { var p = (Project)m; return $"{p.Code} · {p.Name}"; },
                    db => db.Projects.Cast<object>());
                MainArea.Content = _currentView;
                break;
            case "NavCategory":
                _currentView = new GenericRecordView(_db, _metadata, "CostCategory", typeof(CostCategory),
                    new[]
                    {
                        new CoreFieldDescriptor("科目编码", "Code", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("科目名称", "Name", CoreFieldKind.Text, IsRequired: true),
                        new CoreFieldDescriptor("类型", "Type", CoreFieldKind.Enum, EnumType: typeof(CostCategoryType)),
                    },
                    () => new CostCategory(),
                    m => { var c = (CostCategory)m; return string.IsNullOrEmpty(c.FullPath) ? $"{c.Code} · {c.Name}" : c.FullPath; },
                    db => db.CostCategories.Cast<object>(),
                    parentField: "ParentId", pathField: "FullPath");
                MainArea.Content = _currentView;
                break;
            case "NavReports":
                _currentView = new ReportCenterView(_reports);
                MainArea.Content = _currentView;
                break;
            case "NavBoardDesign":
                _currentView = new DashboardSettingsView(_dashboard);
                MainArea.Content = _currentView;
                break;
            case "NavAlerts":
                _currentView = new AlertCenterView(_alerts, _scheduler);
                MainArea.Content = _currentView;
                break;
            case "NavRecon":
                _currentView = new ReconciliationView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavSettings":
                _currentView = new SystemSettingsView(_metadata, _db);
                MainArea.Content = _currentView;
                break;
            case "NavObjects":
                _currentView = new DynamicEntityView(_db, _metadata);
                MainArea.Content = _currentView;
                break;
            default:
                var vm = new DashboardViewModel(_spine, _dashboard);
                var dash = new DashboardView { DataContext = vm };
                MainArea.Content = dash;
                _currentView = dash;
                _ = vm.LoadAsync();
                break;
        }
    }
}
