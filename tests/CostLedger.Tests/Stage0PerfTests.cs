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
/// 阶段0 性能地基：① 编译反射委托（PropertyAccessor）读写与反射语义一致、属性缺失有明确报错；
/// ② MetadataService 内存缓存在写入（字典项/字段/实体）后失效，读取能反映新数据。
/// </summary>
public class Stage0PerfTests
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

    private sealed class Sample
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public DateOnly Day { get; set; }
        public bool Flag { get; set; }
    }

    [Fact]
    public void 编译委托_读写各类型属性与反射语义一致()
    {
        var s = new Sample { Id = 7, Name = "甲", Amount = 12.5m, Day = new DateOnly(2026, 1, 2), Flag = true };

        PropertyAccessor.Get(s, "Id").Should().Be(7);
        PropertyAccessor.Get(s, "Name").Should().Be("甲");
        PropertyAccessor.Get(s, "Amount").Should().Be(12.5m);
        PropertyAccessor.Get(s, "Day").Should().Be(new DateOnly(2026, 1, 2));
        PropertyAccessor.Get(s, "Flag").Should().Be(true);

        PropertyAccessor.Set(s, "Id", 9);
        PropertyAccessor.Set(s, "Name", "乙");
        PropertyAccessor.Set(s, "Amount", 3m);
        PropertyAccessor.Set(s, "Day", new DateOnly(2026, 5, 6));
        PropertyAccessor.Set(s, "Flag", false);

        s.Id.Should().Be(9);
        s.Name.Should().Be("乙");
        s.Amount.Should().Be(3m);
        s.Day.Should().Be(new DateOnly(2026, 5, 6));
        s.Flag.Should().BeFalse();
    }

    [Fact]
    public void 编译委托_属性不存在_抛出明确异常()
    {
        var act = () => PropertyAccessor.Get(new Sample(), "Nope");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Nope*");
    }

    [Fact]
    public async Task 元数据缓存_写入后失效_读取反映新数据()
    {
        using var db = CreateContext();
        var svc = new MetadataService(db);

        // 字典：先建定义（写入使缓存失效），读回（重载进缓存），再加项 → 读取须含新项
        await svc.SaveLookupDefAsync(new LookupDef { Code = "TestUnit", Name = "测试单位" });
        var def = (await svc.ListLookupDefsAsync()).First(x => x.Code == "TestUnit");
        (await svc.ListLookupItemsAsync("TestUnit")).Should().BeEmpty();

        await svc.SaveLookupItemAsync(new LookupItem { LookupDefId = def.Id, Code = "m", Name = "米", SortOrder = 1 });
        (await svc.ListLookupItemsAsync("TestUnit")).Should().ContainSingle(i => i.Name == "米");

        // 实体/字段：同一路径验证失效
        await svc.SaveEntityDefAsync(new EntityDef { Code = "CAR", Name = "车辆", IsUserDefined = true });
        (await svc.ListUserEntityDefsAsync()).Should().Contain(e => e.Code == "CAR");

        (await svc.ListFieldDefsAsync("CAR")).Should().BeEmpty();
        await svc.SaveFieldDefAsync(new FieldDef { EntityDefCode = "CAR", EntityDefId = (await svc.ListEntityDefsAsync()).First(e => e.Code == "CAR").Id, Name = "车牌", Type = FieldType.Text });
        (await svc.ListFieldDefsAsync("CAR")).Should().ContainSingle(f => f.Name == "车牌");
    }
}
