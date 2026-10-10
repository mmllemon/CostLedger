using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// 阶段2 系统参数服务：运行时 key-value 配置（公司名称等）的后台读写。
/// 与元数据缓存同一模式：惰性整体载入内存，写入后失效重载——读路径零数据库往返。
/// 铁律：只放"边框与标签"类参数，绝不承载成本脊柱算账参数。
/// </summary>
public class SystemParamService
{
    private readonly AppDbContext _db;
    private readonly object _gate = new();
    private Dictionary<string, string> _cache = new();
    private bool _loaded;

    public SystemParamService(AppDbContext db) => _db = db;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            _cache = _db.SystemParams.AsNoTracking()
                .ToDictionary(p => p.Key, p => p.Value);
            _loaded = true;
        }
    }

    private void Invalidate() => _loaded = false;

    /// <summary>全部参数（键→值），供参数维护页整表展示。</summary>
    public Task<Dictionary<string, string>> GetAllAsync()
    {
        EnsureLoaded();
        return Task.FromResult(new Dictionary<string, string>(_cache));
    }

    /// <summary>按键取值；不存在返回 null。</summary>
    public Task<string?> GetAsync(string key)
    {
        EnsureLoaded();
        return Task.FromResult<string?>(_cache.TryGetValue(key, out var v) ? v : null);
    }

    /// <summary>按键取值；不存在或为空返回默认值。</summary>
    public Task<string> GetOrDefaultAsync(string key, string fallback)
    {
        EnsureLoaded();
        return Task.FromResult(_cache.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback);
    }

    /// <summary>新增/更新参数（按键 upsert）。可同时补用途说明。</summary>
    public async Task SetAsync(string key, string value, string? description = null)
    {
        key = key.Trim();
        var row = await _db.SystemParams.FirstOrDefaultAsync(p => p.Key == key);
        if (row is null)
        {
            _db.SystemParams.Add(new SystemParam { Key = key, Value = value, Description = description });
        }
        else
        {
            row.Value = value;
            if (description != null) row.Description = description;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        Invalidate();
    }

    /// <summary>参数维护页整表保存：逐条 upsert；value 为空且原有 → 保留空值（允许显式清空）。</summary>
    public async Task SaveAllAsync(IEnumerable<(string Key, string Value, string? Description)> items)
    {
        foreach (var (key, value, description) in items)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            await SetAsync(key, value, description);
        }
    }

    public async Task DeleteAsync(string key)
    {
        var row = await _db.SystemParams.FirstOrDefaultAsync(p => p.Key == key);
        if (row is null) return;
        _db.SystemParams.Remove(row);
        await _db.SaveChangesAsync();
        Invalidate();
    }
}
