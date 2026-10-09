using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>部位（WBS / 分部分项）服务：树形维护，自动维护 FullPath。</summary>
public class WbsService
{
    private readonly AppDbContext _db;
    public WbsService(AppDbContext db) => _db = db;

    public Task<List<WBSItem>> ListAsync(int projectId)
        => _db.WbsItems.Where(w => w.ProjectId == projectId)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Code).ToListAsync();

    public async Task<int> SaveAsync(WBSItem w)
    {
        w.FullPath = await BuildPathAsync(w);
        if (w.Id == 0) _db.WbsItems.Add(w);
        else _db.WbsItems.Update(w);
        await _db.SaveChangesAsync();
        return w.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var w = await _db.WbsItems.FindAsync(id);
        if (w is null) return;
        _db.WbsItems.Remove(w);
        await _db.SaveChangesAsync();
    }

    private async Task<string> BuildPathAsync(WBSItem w)
    {
        if (w.ParentId is null) return w.Name;
        var parent = await _db.WbsItems.FindAsync(w.ParentId.Value);
        return parent is null ? w.Name : $"{parent.FullPath}/{w.Name}";
    }
}
