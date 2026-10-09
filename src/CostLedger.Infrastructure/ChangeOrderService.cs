using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 变更令服务：CRUD + 确认/反确认。
/// 口径（2026-10-09 用户确认）：变更令是**目标成本调整**（预算侧），不是"已发生"。
/// 仅 Confirmed 状态的变更令会计入成本脊柱的目标口径（见 CostSpineService），
/// 因此无"重复记账"之虞——状态即真相，反确认即回落。不写 CostEntry（不触碰凭证式已发生）。
/// </summary>
public class ChangeOrderService
{
    private readonly AppDbContext _db;
    public ChangeOrderService(AppDbContext db) => _db = db;

    public Task<List<ChangeOrder>> ListAsync(int projectId)
        => _db.ChangeOrders.Where(x => x.ProjectId == projectId).OrderBy(x => x.Code).ToListAsync();

    public async Task<int> SaveAsync(ChangeOrder co)
    {
        if (co.Id == 0) _db.ChangeOrders.Add(co);
        else _db.ChangeOrders.Update(co);
        await _db.SaveChangesAsync();
        return co.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var co = await _db.ChangeOrders.FindAsync(id);
        if (co is null) return;
        _db.ChangeOrders.Remove(co);
        await _db.SaveChangesAsync();
    }

    /// <summary>确认：计入目标成本口径。已确认则返回 false（幂等）。</summary>
    public async Task<bool> ConfirmAsync(int id)
    {
        var co = await _db.ChangeOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (co is null) return false;
        if (co.Status == DocStatus.Confirmed) return false;
        co.Status = DocStatus.Confirmed;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>反确认：回到草稿，目标成本口径随之回落。</summary>
    public async Task<bool> ReopenAsync(int id)
    {
        var co = await _db.ChangeOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (co is null || co.Status != DocStatus.Confirmed) return false;
        co.Status = DocStatus.Draft;
        await _db.SaveChangesAsync();
        return true;
    }
}
