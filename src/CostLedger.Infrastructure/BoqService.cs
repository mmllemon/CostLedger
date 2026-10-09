using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 工程量清单（BOQ）服务 —— 目标成本的来源与编制入口。
/// 逐项 量 × 综合单价 = 合价；项目有清单行时，成本脊柱"目标"以清单为准。
/// </summary>
public class BoqService
{
    private readonly AppDbContext _db;
    public BoqService(AppDbContext db) => _db = db;

    public Task<List<BoqLine>> ListAsync(int projectId)
        => _db.BoqLines.Where(b => b.ProjectId == projectId)
            .OrderBy(b => b.Code).ToListAsync();

    public async Task<int> SaveAsync(BoqLine b)
    {
        if (b.Amount == 0 && b.Quantity != 0 && b.UnitPrice != 0)
            b.Amount = b.Quantity * b.UnitPrice;

        if (b.Id == 0) _db.BoqLines.Add(b);
        else _db.BoqLines.Update(b);
        await _db.SaveChangesAsync();
        return b.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var b = await _db.BoqLines.FindAsync(id);
        if (b is null) return;
        _db.BoqLines.Remove(b);
        await _db.SaveChangesAsync();
    }

    /// <summary>项目清单合价合计（目标成本基数）。</summary>
    public async Task<decimal> TotalAsync(int projectId)
        => await _db.BoqLines.Where(b => b.ProjectId == projectId).SumAsync(b => b.Amount);
}
