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
/// 阶段2：① 字典启用/停用可见性规则（禁用项不出现、历史值保留显示）；
/// ② 系统参数（key-value）服务：upsert/缓存失效/改键删除/默认值兜底。
/// </summary>
public class Stage2Tests
{
    private static AppDbContext CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        SeedData.SeedIfEmpty(db);
        return db;
    }

    private static LookupItem Item(string code, string name, bool enabled = true)
        => new() { Code = code, Name = name, IsEnabled = enabled, SortOrder = 1 };

    [Fact]
    public void 字典可见性_禁用项不出现_历史值保留显示()
    {
        var items = new List<LookupItem>
        {
            Item("A", "甲"), Item("B", "乙", enabled: false), Item("C", "丙"),
        };

        // 新录入：只见启用项
        LookupFilter.Visible(items, null).Select(i => i.Code).Should().BeEquivalentTo(new[] { "A", "C" });

        // 编辑历史数据：当前值是停用项 B → 仍保留显示（防静默丢值）
        var vis = LookupFilter.Visible(items, "B");
        vis.Select(i => i.Code).Should().BeEquivalentTo(new[] { "A", "C", "B" });

        // 停用的非当前值不出现
        LookupFilter.Visible(items, "A").Select(i => i.Code).Should().BeEquivalentTo(new[] { "A", "C" });

        // 空列表安全
        LookupFilter.Visible(null, "A").Should().BeEmpty();
        LookupFilter.Visible(new List<LookupItem>(), "A").Should().BeEmpty();

        // 默认启用
        new LookupItem().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task 系统参数_写入读取_更新_改键_删除()
    {
        using var db = CreateContext();
        var svc = new SystemParamService(db);

        // 种子参数存在且值为空
        (await svc.GetAsync("公司名称")).Should().Be("");

        // upsert 写入 → 读回（缓存写后失效）
        await svc.SetAsync("公司名称", "重庆诺亨实业有限公司", "测试说明");
        (await svc.GetAsync("公司名称")).Should().Be("重庆诺亨实业有限公司");

        // 同键再写 = 更新
        await svc.SetAsync("公司名称", "诺亨");
        (await svc.GetAsync("公司名称")).Should().Be("诺亨");
        (await db.SystemParams.CountAsync(p => p.Key == "公司名称")).Should().Be(1);

        // GetOrDefault：空值/缺键走兜底
        (await svc.GetOrDefaultAsync("公司地址", "（未填）")).Should().Be("（未填）");
        (await svc.GetOrDefaultAsync("公司名称", "（未填）")).Should().Be("诺亨");

        // 改键：旧键删除、新键写入（走 SaveAsync 等价路径：Set 新键 + Delete 旧键）
        await svc.SetAsync("公司全称", "诺亨");
        await svc.DeleteAsync("公司名称");
        (await svc.GetAsync("公司名称")).Should().BeNull();
        (await svc.GetAsync("公司全称")).Should().Be("诺亨");

        // GetAll 反映最终状态
        var all = await svc.GetAllAsync();
        all.Should().ContainKey("公司全称").And.NotContainKey("公司名称");
    }

    [Fact]
    public async Task 元数据_字典项启用停用_经MetadataService往返()
    {
        using var db = CreateContext();
        var svc = new MetadataService(db);

        var items = await svc.ListLookupItemsAsync("PartyRole");
        items.Should().OnlyContain(i => i.IsEnabled);   // 种子默认全启用

        var target = items.First(i => i.Code == "Other");
        target.IsEnabled = false;
        await svc.SaveLookupItemAsync(target);

        // 写后缓存失效 → 读到停用状态；管理页仍返回全部项（过滤只在渲染层）
        var reloaded = await svc.ListLookupItemsAsync("PartyRole");
        reloaded.First(i => i.Code == "Other").IsEnabled.Should().BeFalse();
        reloaded.Should().HaveCount(items.Count);
    }
}
