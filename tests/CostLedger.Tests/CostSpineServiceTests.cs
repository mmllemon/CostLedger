using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 成本脊柱服务测试：用内存 SQLite 建库 + 种子，验证按科目聚合与超支判定（凭证式记账闭环）。
/// </summary>
public class CostSpineServiceTests
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
    public async Task GetSpine_电缆科目_超支判定正确()
    {
        using var db = CreateSeededContext();
        var spine = new CostSpineService(db);
        var lines = await spine.GetSpineAsync(1);

        var cable = lines.Should().ContainSingle(l => l.CostCategoryName == "电力电缆").Subject;
        cable.Target.Should().Be(1_863_000m);
        cable.Incurred.Should().Be(2_000_000m);
        cable.Variance.Should().Be(137_000m);
        cable.IsOver.Should().BeTrue();
    }

    [Fact]
    public async Task GetSpine_分包科目_不超支()
    {
        using var db = CreateSeededContext();
        var spine = new CostSpineService(db);
        var lines = await spine.GetSpineAsync(1);

        var sub = lines.Should().ContainSingle(l => l.CostCategoryName == "分包工程").Subject;
        sub.Target.Should().Be(2_270_000m);
        sub.Incurred.Should().Be(2_270_000m);
        sub.IsOver.Should().BeFalse();
    }

    [Fact]
    public async Task GetSpine_人工科目_剩余为正()
    {
        using var db = CreateSeededContext();
        var spine = new CostSpineService(db);
        var lines = await spine.GetSpineAsync(1);

        var labor = lines.Should().ContainSingle(l => l.CostCategoryName == "人工").Subject;
        labor.Target.Should().Be(500_000m);
        labor.Incurred.Should().Be(400_000m);
        labor.Remaining.Should().Be(100_000m);
    }
}
