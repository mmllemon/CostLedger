using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// M2-A 配置中心服务：字典与字段定义的 CRUD + 扩展字段值读写。
/// 仅操作元数据层，绝不触及成本脊柱算账逻辑（铁律）。
/// </summary>
public class MetadataService
{
    private readonly AppDbContext _db;
    public MetadataService(AppDbContext db) => _db = db;

    // ---------- 字典 ----------
    public Task<List<LookupDef>> ListLookupDefsAsync()
        => _db.LookupDefs.Include(x => x.Items).OrderBy(x => x.Name).ToListAsync();

    public async Task SaveLookupDefAsync(LookupDef def)
    {
        if (def.Id == 0) _db.LookupDefs.Add(def);
        else _db.LookupDefs.Update(def);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteLookupDefAsync(int id)
    {
        var def = await _db.LookupDefs.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (def is null) return;
        _db.LookupDefs.Remove(def);
        await _db.SaveChangesAsync();
    }

    public async Task SaveLookupItemAsync(LookupItem item)
    {
        if (item.Id == 0) _db.LookupItems.Add(item);
        else _db.LookupItems.Update(item);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteLookupItemAsync(int id)
    {
        var item = await _db.LookupItems.FindAsync(id);
        if (item is null) return;
        _db.LookupItems.Remove(item);
        await _db.SaveChangesAsync();
    }

    public Task<List<LookupItem>> ListLookupItemsAsync(string lookupDefCode)
        => _db.LookupItems.Where(x => x.LookupDef!.Code == lookupDefCode).OrderBy(x => x.SortOrder).ToListAsync();

    // ---------- 实体 / 字段定义 ----------
    public Task<List<EntityDef>> ListEntityDefsAsync()
        => _db.EntityDefs.OrderBy(x => x.Name).ToListAsync();

    public Task<List<FieldDef>> ListFieldDefsAsync(string entityCode)
        => _db.FieldDefs.Where(x => x.EntityDefCode == entityCode).OrderBy(x => x.DisplayOrder).ToListAsync();

    public async Task SaveFieldDefAsync(FieldDef f)
    {
        if (f.Id == 0) _db.FieldDefs.Add(f);
        else _db.FieldDefs.Update(f);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteFieldDefAsync(int id)
    {
        var f = await _db.FieldDefs.FindAsync(id);
        if (f is null) return;
        _db.FieldDefs.Remove(f);
        await _db.SaveChangesAsync();
    }

    // ---------- 扩展字段值（EAV） ----------
    public async Task<Dictionary<int, string>> GetCustomValuesAsync(string entityCode, int ownerId)
        => (await _db.CustomFieldValues
                .Where(x => x.EntityDefCode == entityCode && x.OwnerId == ownerId)
                .ToListAsync())
            .ToDictionary(x => x.FieldDefId, x => x.Value);

    public async Task SaveCustomValuesAsync(string entityCode, int ownerId, Dictionary<int, string> values)
    {
        var existing = await _db.CustomFieldValues
            .Where(x => x.EntityDefCode == entityCode && x.OwnerId == ownerId).ToListAsync();
        foreach (var (fieldDefId, value) in values)
        {
            var row = existing.FirstOrDefault(x => x.FieldDefId == fieldDefId);
            if (row is null)
            {
                _db.CustomFieldValues.Add(new CustomFieldValue
                {
                    EntityDefCode = entityCode,
                    OwnerId = ownerId,
                    FieldDefId = fieldDefId,
                    Value = value
                });
            }
            else
            {
                row.Value = value;
            }
        }
        // 清理被移除的字段值
        foreach (var row in existing)
        {
            if (!values.ContainsKey(row.FieldDefId))
                _db.CustomFieldValues.Remove(row);
        }
        await _db.SaveChangesAsync();
    }
}
