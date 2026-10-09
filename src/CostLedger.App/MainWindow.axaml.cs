using Avalonia;
using Avalonia.Controls;
using CostLedger.App.ViewModels;
using CostLedger.App.Views;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;

namespace CostLedger.App;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db = null!;
    private readonly ICostSpineService _spine = null!;
    private readonly MetadataService _metadata = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ICostSpineService spine, AppDbContext db, MetadataService metadata)
    {
        _db = db;
        _spine = spine;
        _metadata = metadata;
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
        foreach (var b in new[] { NavDashboard, NavMaster, NavSettings })
            b.Classes.Remove("on");

        switch (key)
        {
            case "NavMaster":
                NavMaster.Classes.Add("on");
                MainArea.Content = new MasterDataView(_db, _metadata);
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
