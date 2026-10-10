using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
            // 用环境变量 COSTLEDGER_DB 指定其它账套（例如导入真实数据生成的账套），
            // 便于在不覆盖默认账套的前提下打开指定数据文件。
            var overridePath = Environment.GetEnvironmentVariable("COSTLEDGER_DB");
            if (!string.IsNullOrWhiteSpace(overridePath)) dbPath = overridePath!;
            var options = DbBootstrap.Configure(dbPath);
            var db = new AppDbContext(options);
            try
            {
                DbBootstrap.EnsureCreatedAndSeed(db, dbPath);
            }
            catch (InvalidOperationException ex)
            {
                db.Dispose();
                var close = new Button { Content = "关闭", Classes = { "primary" } };
                var errorWindow = new Window
                {
                    Title = "CostLedger · 无法打开账套",
                    Width = 620,
                    Height = 300,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24),
                        Spacing = 18,
                        Children =
                        {
                            new TextBlock { Text = "账套未打开", FontSize = 22 },
                            new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                            close
                        }
                    }
                };
                close.Click += (_, _) => errorWindow.Close();
                desktop.MainWindow = errorWindow;
                base.OnFrameworkInitializationCompleted();
                return;
            }
            ICostSpineService spine = new CostSpineService(db);
            var metadata = new MetadataService(db);

            desktop.MainWindow = new MainWindow(spine, db, metadata);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
