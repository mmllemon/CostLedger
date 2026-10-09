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

/// <summary>审计测试：增/改/删自动留痕；种子不写审计。</summary>
public class AuditTests
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

    [Fact]
    public void 种子不写审计()
    {
        using var db = CreateSeededContext();
        db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task 增改删自动留痕()
    {
        using var db = CreateSeededContext();

        var co = new ChangeOrder { Code = "CO-AUD", ProjectId = 1, Title = "审计用", Amount = 1000m, Type = ChangeOrderType.DesignChange, Status = DocStatus.Draft, OrderDate = new DateOnly(2026, 3, 1) };
        db.ChangeOrders.Add(co);
        await db.SaveChangesAsync();

        co.Amount = 2000m;
        db.ChangeOrders.Update(co);
        await db.SaveChangesAsync();

        db.ChangeOrders.Remove(co);
        await db.SaveChangesAsync();

        var logs = await new AuditService(db).ListAsync("ChangeOrder");
        logs.Should().HaveCount(3);
        logs.Select(l => l.Action).Should().Contain(new[] { AuditAction.Create, AuditAction.Update, AuditAction.Delete });
        logs.Should().OnlyContain(l => l.EntityName == "ChangeOrder" && !string.IsNullOrEmpty(l.By));
    }
}
