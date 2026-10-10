using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// M2-A 配置中心服务：字典与字段定义的 CRUD + 扩展字段值读写。
/// 仅操作元数据层，绝不触及成本脊柱算账逻辑（铁律）。
///
/// 阶段0 性能地基：EntityDef(含 Fields) 与 LookupItems 在首次访问时整体载入内存并缓存，
/// 后续读路径零数据库往返；任何写（建/删实体、字段、字典）后失效并在下次读取时惰性重载。
/// —— 消除 DynamicEntityView 按字段逐个查字典、GenericRecordView 取字典等的重复 IO。
/// </summary>
public class MetadataService
{
    private readonly AppDbContext _db;
    private readonly object _gate = new();
    private Dictionary<string, EntityDef> _entityByCode = new();
    private Dictionary<string, LookupDef> _lookupByCode = new();
    private bool _loaded;

    public MetadataService(AppDbContext db) => _db = db;

    // ---------- 缓存加载 / 失效 ----------
    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            var ents = _db.EntityDefs.Include(e => e.Fields).OrderBy(x => x.Name).ToList();
            _entityByCode = ents.ToDictionary(e => e.Code);
            var lookups = _db.LookupDefs.Include(x => x.Items).ToList();
            foreach (var l in lookups)
                l.Items = l.Items.OrderBy(i => i.SortOrder).ToList();
            _lookupByCode = lookups.ToDictionary(l => l.Code);
            _loaded = true;
        }
    }

    /// <summary>强制下次读取重新从库加载（外部绕过本服务改了元数据时使用）。</summary>
    public void Invalidate() => _loaded = false;

    private void InvalidateAfterWrite() => _loaded = false;

    // ---------- 字典 ----------
    public Task<List<LookupDef>> ListLookupDefsAsync()
    {
        EnsureLoaded();
        return Task.FromResult(_lookupByCode.Values.OrderBy(x => x.Name).ToList());
    }

    public async Task SaveLookupDefAsync(LookupDef def)
    {
        if (def.Id == 0) _db.LookupDefs.Add(def);
        else _db.LookupDefs.Update(def);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public async Task DeleteLookupDefAsync(int id)
    {
        var def = await _db.LookupDefs.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (def is null) return;
        _db.LookupDefs.Remove(def);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public async Task SaveLookupItemAsync(LookupItem item)
    {
        if (item.Id == 0) _db.LookupItems.Add(item);
        else _db.LookupItems.Update(item);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public async Task DeleteLookupItemAsync(int id)
    {
        var item = await _db.LookupItems.FindAsync(id);
        if (item is null) return;
        _db.LookupItems.Remove(item);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public Task<List<LookupItem>> ListLookupItemsAsync(string lookupDefCode)
    {
        EnsureLoaded();
        return Task.FromResult(
            _lookupByCode.TryGetValue(lookupDefCode, out var def) ? def.Items.ToList() : new List<LookupItem>());
    }

    // ---------- 实体 / 字段定义 ----------
    public Task<List<EntityDef>> ListEntityDefsAsync()
    {
        EnsureLoaded();
        return Task.FromResult(_entityByCode.Values.OrderBy(x => x.Name).ToList());
    }

    public Task<List<EntityDef>> ListUserEntityDefsAsync()
    {
        EnsureLoaded();
        return Task.FromResult(_entityByCode.Values.Where(e => e.IsUserDefined).OrderBy(x => x.Name).ToList());
    }

    public async Task<int> SaveEntityDefAsync(EntityDef def)
    {
        if (def.Id == 0) _db.EntityDefs.Add(def);
        else _db.EntityDefs.Update(def);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
        return def.Id;
    }

    public async Task DeleteEntityDefAsync(int id)
    {
        var def = await _db.EntityDefs.FirstOrDefaultAsync(x => x.Id == id);
        if (def is null) return;
        // 级联删除其记录与字段（FieldDef 随实体级联）
        var recs = await _db.DynamicRecords.Where(r => r.EntityDefCode == def.Code).ToListAsync();
        _db.DynamicRecords.RemoveRange(recs);
        _db.EntityDefs.Remove(def);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public Task<List<FieldDef>> ListFieldDefsAsync(string entityCode)
    {
        EnsureLoaded();
        return Task.FromResult(
            _entityByCode.TryGetValue(entityCode, out var e)
                ? e.Fields.OrderBy(f => f.DisplayOrder).ToList()
                : new List<FieldDef>());
    }

    public async Task SaveFieldDefAsync(FieldDef f)
    {
        if (f.Id == 0) _db.FieldDefs.Add(f);
        else _db.FieldDefs.Update(f);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    public async Task DeleteFieldDefAsync(int id)
    {
        var f = await _db.FieldDefs.FindAsync(id);
        if (f is null) return;
        _db.FieldDefs.Remove(f);
        await _db.SaveChangesAsync();
        InvalidateAfterWrite();
    }

    // ---------- 扩展字段值（EAV，按 owner 读写，不缓存） ----------
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
