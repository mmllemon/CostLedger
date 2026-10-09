using Avalonia;
using Avalonia.Controls;
using CostLedger.App.ViewModels;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;

namespace CostLedger.App;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ICostSpineService spine, AppDbContext db)
    {
        _db = db;
        InitializeComponent();
        var vm = new DashboardViewModel(spine);
        Dashboard.DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
        Closed += (_, _) => _db.Dispose();
    }
}
