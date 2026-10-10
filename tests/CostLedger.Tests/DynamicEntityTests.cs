using CostLedger.Domain;
using CostLedger.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 阶段1 运行时自建对象：建类型 → 录记录 → EAV 读回 → 删除；删类型级联删记录与字段。
/// </summary>
public class DynamicEntityTests
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
    public async Task 自建对象_建类型_录记录_读回_删除()
    {
        using var db = CreateSeededContext();
        var svc = new DynamicEntityService(db);

        var eid = await svc.CreateUserEntityAsync("设备台账", "EQUIP", new List<(string, FieldType)>
        {
            ("规格型号", FieldType.Text),
            ("原值", FieldType.Money),
            ("购置日期", FieldType.Date),
        });
        eid.Should().BeGreaterThan(0);

        var fields = await svc.ListFieldsAsync("EQUIP");
        fields.Should().HaveCount(3);
        fields.Should().OnlyContain(f => f.EntityDefCode == "EQUIP" && !f.IsCore);

        var moneyF = fields.First(f => f.Name == "原值");
        var dateF = fields.First(f => f.Name == "购置日期");
        var rid = await svc.SaveRecordAsync("EQUIP", 0, "EQ-001", "变压器1号",
            new Dictionary<int, string> { [moneyF.Id] = "120000", [dateF.Id] = "2026-01-15" });
        rid.Should().BeGreaterThan(0);

        (await svc.ListRecordsAsync("EQUIP")).Should().ContainSingle(r => r.Name == "变压器1号");

        var (rec, vals) = await svc.GetRecordAsync(rid);
        rec.Code.Should().Be("EQ-001");
        vals[moneyF.Id].Should().Be("120000");
        vals[dateF.Id].Should().Be("2026-01-15");

        (await svc.ListUserEntitiesAsync()).Should().Contain(e => e.Code == "EQUIP" && e.IsUserDefined);

        await svc.DeleteRecordAsync(rid);
        (await svc.ListRecordsAsync("EQUIP")).Should().NotContain(r => r.Id == rid);
    }

    [Fact]
    public async Task 自建对象_删除类型_级联删记录与字段()
    {
        using var db = CreateSeededContext();
        var svc = new DynamicEntityService(db);
        await svc.CreateUserEntityAsync("资质", "CERT", new List<(string, FieldType)> { ("有效期", FieldType.Date) });
        var fields = await svc.ListFieldsAsync("CERT");
        await svc.SaveRecordAsync("CERT", 0, "C1", "安全生产许可证", new Dictionary<int, string> { [fields[0].Id] = "2027-01-01" });

        var def = (await svc.ListUserEntitiesAsync()).First(e => e.Code == "CERT");
        await svc.DeleteEntityAsync(def.Id);

        (await svc.ListUserEntitiesAsync()).Should().NotContain(e => e.Code == "CERT");
        (await svc.ListRecordsAsync("CERT")).Should().BeEmpty();
        (await db.FieldDefs.CountAsync(f => f.EntityDefCode == "CERT")).Should().Be(0);
    }
}
