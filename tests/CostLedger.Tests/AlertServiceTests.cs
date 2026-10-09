using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 预警引擎与调度测试：规则触发、记录去重、确认后重触发、到期运行（M3-A）。
/// </summary>
public class AlertServiceTests
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

    private static AlertService CreateAlertService(AppDbContext db)
        => new(db, new DashboardService(db, new CostSpineService(db)));

    [Fact]
    public async Task 评估_触发两条规则_重复评估不重复堆积()
    {
        using var db = CreateSeededContext();
        var svc = CreateAlertService(db);

        var first = await svc.EvaluateAsync();
        first.Should().Be(2); // 超支科目数≥1 与 总偏差≥3万 均触发

        var second = await svc.EvaluateAsync();
        second.Should().Be(0); // 去重：同规则+项目未确认记录不重复新增

        (await svc.ListAlertsAsync(onlyUnacknowledged: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task 确认后再评估_重新生成一条()
    {
        using var db = CreateSeededContext();
        var svc = CreateAlertService(db);
        await svc.EvaluateAsync();

        var list = await svc.ListAlertsAsync(onlyUnacknowledged: true);
        await svc.AcknowledgeAsync(list[0].Id);

        var created = await svc.EvaluateAsync();
        created.Should().Be(1); // 被确认的那条不再是"未确认"，重新触发

        (await svc.ListAlertsAsync(onlyUnacknowledged: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task 调度_到期运行任务并记录()
    {
        using var db = CreateSeededContext();
        var scheduler = new SchedulerService(db, CreateAlertService(db));

        var ran = await scheduler.RunDueAsync(DateTime.UtcNow);
        ran.Should().Be(1); // 种子任务首次到期

        var task = (await scheduler.ListTasksAsync()).Single();
        task.LastRunAt.Should().NotBeNull();
        task.LastRunCount.Should().Be(2); // 本次产出 2 条预警

        var again = await scheduler.RunDueAsync(DateTime.UtcNow);
        again.Should().Be(0); // 未到间隔期，不再运行
    }
}
