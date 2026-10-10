using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 阶段1 运行时自建对象服务：用户在「自建对象」中新建的"对象类型"与其记录的 CRUD + EAV 值读写。
/// 与 GenericRecordView（系统实体）平行，但数据落在 DynamicRecord/DynamicValue（EAV），无需改表结构、免编译。
/// 铁律：仅作通用数据收纳，绝不参与成本脊柱归集。
/// </summary>
public class DynamicEntityService
{
    private readonly AppDbContext _db;
    public DynamicEntityService(AppDbContext db) => _db = db;

    public Task<List<EntityDef>> ListUserEntitiesAsync()
        => _db.EntityDefs.Where(e => e.IsUserDefined).OrderBy(e => e.Name).ToListAsync();

    public Task<List<FieldDef>> ListFieldsAsync(string entityCode)
        => _db.FieldDefs.Where(f => f.EntityDefCode == entityCode).OrderBy(f => f.DisplayOrder).ToListAsync();

    public Task<List<DynamicRecord>> ListRecordsAsync(string entityCode)
        => _db.DynamicRecords.Where(r => r.EntityDefCode == entityCode).OrderBy(r => r.Name).ToListAsync();

    /// <summary>新建一个用户对象类型，并附带若干字段（默认文本型）。原子写入。</summary>
    public async Task<int> CreateUserEntityAsync(string name, string code, List<(string Name, FieldType Type)> fields)
    {
        var def = new EntityDef { Code = code, Name = name, IsUserDefined = true };
        _db.EntityDefs.Add(def);
        await _db.SaveChangesAsync();
        int order = 1;
        foreach (var f in fields)
            _db.FieldDefs.Add(new FieldDef
            {
                EntityDefId = def.Id,
                EntityDefCode = def.Code,
                Name = f.Name,
                Type = f.Type,
                IsCore = false,
                DisplayOrder = order++
            });
        await _db.SaveChangesAsync();
        return def.Id;
    }

    public async Task<int> SaveRecordAsync(string entityCode, int id, string code, string name, Dictionary<int, string> values)
    {
        DynamicRecord rec;
        if (id == 0)
        {
            rec = new DynamicRecord { EntityDefCode = entityCode, Code = code, Name = name, CreatedAt = DateTime.UtcNow };
            _db.DynamicRecords.Add(rec);
            await _db.SaveChangesAsync();
        }
        else
        {
            rec = await _db.DynamicRecords.FirstAsync(r => r.Id == id);
            rec.Code = code;
            rec.Name = name;
            await _db.SaveChangesAsync();
        }

        var existing = await _db.DynamicValues.Where(v => v.RecordId == rec.Id).ToListAsync();
        foreach (var (fid, val) in values)
        {
            var row = existing.FirstOrDefault(v => v.FieldDefId == fid);
            if (row is null)
                _db.DynamicValues.Add(new DynamicValue { RecordId = rec.Id, FieldDefId = fid, Value = val });
            else
                row.Value = val;
        }
        foreach (var row in existing)
            if (!values.ContainsKey(row.FieldDefId))
                _db.DynamicValues.Remove(row);
        await _db.SaveChangesAsync();
        return rec.Id;
    }

    public async Task<(DynamicRecord Record, Dictionary<int, string> Values)> GetRecordAsync(int id)
    {
        var rec = await _db.DynamicRecords.FirstAsync(r => r.Id == id);
        var vals = (await _db.DynamicValues.Where(v => v.RecordId == id).ToListAsync())
            .ToDictionary(v => v.FieldDefId, v => v.Value);
        return (rec, vals);
    }

    public async Task DeleteRecordAsync(int id)
    {
        var rec = await _db.DynamicRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (rec is null) return;
        _db.DynamicValues.RemoveRange(await _db.DynamicValues.Where(v => v.RecordId == id).ToListAsync());
        _db.DynamicRecords.Remove(rec);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteEntityAsync(int id)
    {
        var def = await _db.EntityDefs.FirstOrDefaultAsync(e => e.Id == id);
        if (def is null) return;
        var recs = await _db.DynamicRecords.Where(r => r.EntityDefCode == def.Code).ToListAsync();
        _db.DynamicRecords.RemoveRange(recs);
        _db.EntityDefs.Remove(def); // FieldDefs 级联
        await _db.SaveChangesAsync();
    }
}
