using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 竣工结算服务：CRUD + 结算金额汇总。
/// 用于锁定「预计最终」口径（Settlement.SettledAmount）。
/// </summary>
public class SettlementService
{
    private readonly AppDbContext _db;
    public SettlementService(AppDbContext db) => _db = db;

    public Task<List<Settlement>> ListAsync(int projectId)
        => _db.Settlements.Where(x => x.ProjectId == projectId).OrderBy(x => x.Code).ToListAsync();

    public async Task<int> SaveAsync(Settlement s)
    {
        if (s.SettledAmount == 0)
            s.SettledAmount = s.OriginalAmount + s.AdjustAmount;
        if (s.Id == 0) _db.Settlements.Add(s);
        else _db.Settlements.Update(s);
        await _db.SaveChangesAsync();
        return s.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var s = await _db.Settlements.FindAsync(id);
        if (s is null) return;
        _db.Settlements.Remove(s);
        await _db.SaveChangesAsync();
    }

    /// <summary>项目已审定结算金额合计（Status=Confirmed）。</summary>
    public Task<decimal> SettledTotalAsync(int projectId)
        => _db.Settlements
            .Where(x => x.ProjectId == projectId && x.Status == DocStatus.Confirmed)
            .SumAsync(x => x.SettledAmount);
}
