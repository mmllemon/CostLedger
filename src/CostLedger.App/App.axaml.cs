using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CostLedger.App.Views;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.IO;

namespace CostLedger.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "costledger.db");
            var options = DbBootstrap.Configure(dbPath);
            var db = new AppDbContext(options);
            DbBootstrap.EnsureCreatedAndSeed(db);
            ICostSpineService spine = new CostSpineService(db);

            desktop.MainWindow = new MainWindow(spine, db);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
