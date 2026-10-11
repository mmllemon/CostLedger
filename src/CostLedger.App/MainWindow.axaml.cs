using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        // 视图初始化里的异步异常不应让整个程序退出：记录后继续运行，用户可切到别的页面。
        Dispatcher.UIThread.UnhandledException += (_, ev) =>
        {
            Console.Error.WriteLine("[UI 线程异常] " + ev.Exception);
            ShowError("页面发生错误", ev.Exception);
            ev.Handled = true;
        };
        StartScheduler();
        _ = LoadProjectsAsync();
        NavigateTo("NavCompany");
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateMaxGlyph();
        };
    }

    /// <summary>自绘标题栏拖动：双击切换最大化/还原，否则按系统方式拖动窗口。</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnToggleMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxGlyph();
    }

    private void UpdateMaxGlyph()
    {
        if (WinMaxGlyph is not null)
            WinMaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    /// <summary>把异常就地显示在主区，而不是让程序崩溃退出。</summary>
    private void ShowError(string title, Exception? ex)
    {
        try
        {
            MainArea.Content = new ScrollViewer
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F4EEE5")),
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(24),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                        new TextBlock
                        {
                            Text = ex?.Message ?? "未知错误",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#BE4A3C"))
                        },
                        new TextBlock
                        {
                            Text = "该页面暂不可用，已阻止其影响其它页面。请切换到其它导航项。",
                            FontSize = 12,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6B635A"))
                        }
                    }
                }
            };
        }
        catch { /* 显示错误都不能再抛出 */ }
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
        foreach (var b in new[] { NavCompany, NavProjects, NavCockpit, NavCostControl, NavRegister, NavForecast, NavChangeItems, NavOverview, NavLedgers, NavParties, NavCost, NavMaster, NavDocs, NavCategory, NavReports, NavExternalResults, NavAlerts, NavRecon, NavSettings, NavAdmin, NavObjects })
            b.Classes.Remove("on");
        var target = key switch
        {
            "NavCompany" => NavCompany,
            "NavProjects" => NavProjects,
            "NavCockpit" => NavCockpit,
            "NavCostControl" => NavCostControl,
            "NavRegister" => NavRegister,
            "NavForecast" => NavForecast,
            "NavChangeItems" => NavChangeItems,
            "NavExternalResults" => NavExternalResults,
            "NavOverview" => NavOverview,
            "NavLedgers" => NavLedgers,
            "NavParties" => NavParties,
            "NavCost" => NavCost,
            "NavMaster" => NavMaster,
            "NavDocs" => NavDocs,
            "NavCategory" => NavCategory,
            "NavReports" => NavReports,
            "NavAlerts" => NavAlerts,
            "NavRecon" => NavRecon,
            "NavSettings" => NavSettings,
            "NavAdmin" => NavAdmin,
            "NavObjects" => NavObjects,
            _ => NavCompany
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
            case "NavProjects":
                _currentView = new ProjectManageView(_db);
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
            case "NavRegister":
                Show(new RegisterAmountView(_db));
                break;
            case "NavForecast":
                Show(new ForecastView(_db));
                break;
            case "NavChangeItems":
                Show(new ChangeItemsView(_db));
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
                _currentView = new PartyPanoramaView(_db);
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
                _currentView = new InternalReportView(_db);
                MainArea.Content = _currentView;
                break;
            case "NavExternalResults":
                Show(new ExternalResultsView(_db));
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
                _currentView = new CompanyCockpitView(_db);
                MainArea.Content = _currentView;
                break;
        }
    }
}
