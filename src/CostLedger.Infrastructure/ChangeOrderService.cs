using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 变更令服务：CRUD + 确认/反确认。
/// 确认 → 经回写引擎追加成本分录（幂等）；反确认 → 追加反向分录（不删原分录）。
/// </summary>
public class ChangeOrderService
{
    private readonly AppDbContext _db;
    private readonly CostWriteBackService _writer;

    public ChangeOrderService(AppDbContext db, CostWriteBackService writer)
    {
        _db = db;
        _writer = writer;
    }

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

    /// <summary>确认并回写成本。已确认则返回 false（幂等）。</summary>
    public async Task<bool> ConfirmAsync(int id)
    {
        var co = await _db.ChangeOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (co is null) throw new InvalidOperationException("变更令不存在");
        if (co.Status == DocStatus.Confirmed) return false;
        co.Status = DocStatus.Confirmed;
        await _db.SaveChangesAsync();
        return await _writer.AppendAsync(co.ProjectId, co.CostCategoryId, co.ContractId,
            VoucherType.ChangeOrder, co.Code, co.Amount, co.OrderDate, $"变更令 {co.Title}");
    }

    /// <summary>反确认：冲销成本分录并回到草稿。返回是否发生冲销。</summary>
    public async Task<bool> ReopenAsync(int id)
    {
        var co = await _db.ChangeOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (co is null || co.Status != DocStatus.Confirmed) return false;
        co.Status = DocStatus.Draft;
        await _db.SaveChangesAsync();
        return await _writer.ReverseAsync(co.ProjectId, co.CostCategoryId, co.ContractId,
            VoucherType.ChangeOrder, co.Code, co.Amount, co.OrderDate);
    }
}
