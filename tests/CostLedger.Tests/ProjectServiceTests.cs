using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>项目主数据：编号/名称必填、编号唯一。</summary>
public class ProjectServiceTests
{
    private static AppDbContext CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task 新建项目_必填校验与编号唯一()
    {
        using var db = CreateContext();
        var svc = new ProjectService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateProjectAsync("P-1", "  ", null, new DateOnly(2026, 1, 1), null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateProjectAsync("", "名称", null, new DateOnly(2026, 1, 1), null));

        var p = await svc.CreateProjectAsync("P-1", "10kV外线电缆工程", "某供电公司", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31));
        p.Id.Should().BeGreaterThan(0);
        (await db.Projects.CountAsync()).Should().Be(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateProjectAsync("P-1", "重复编号", null, new DateOnly(2026, 1, 1), null));
    }
}
