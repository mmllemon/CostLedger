using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 看板服务测试：验证默认卡片、阈值判级与配置显隐（M2-D 看板设计器核心）。
/// </summary>
public class DashboardServiceTests
{
    private static AppDbContext CreateSeededContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        SeedData.SeedIfEmpty(db);
        return db;
    }

    private static DashboardService CreateService(AppDbContext db)
        => new(db, new CostSpineService(db));

    [Fact]
    public async Task 默认看板_三张卡片_偏差判为告警()
    {
        using var db = CreateSeededContext();
        var cards = await CreateService(db).GetCardsAsync(1);

        cards.Should().HaveCount(3);
        var variance = cards.Single(c => c.Title == "总偏差");
        variance.Severity.Should().Be(KpiSeverity.Danger); // 种子偏差 37,000 ≥ 告警线 30,000
    }

    [Fact]
    public async Task 配置显隐_仅保留项目数_一张卡片()
    {
        using var db = CreateSeededContext();
        var svc = CreateService(db);

        int i = 0;
        var list = DashboardService.Catalog.Select(m => new DashboardWidget
        {
            WidgetKey = m.Key,
            Title = m.Name,
            Visible = m.Key == "ProjectCount",
            DisplayOrder = i++
        }).ToList();
        await svc.SaveWidgetsAsync(list);

        var cards = await svc.GetCardsAsync(1);
        cards.Should().ContainSingle();
        cards[0].Title.Should().Be("项目数");
        cards[0].ValueText.Should().Be("1 个");
    }
}
