using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 材料收料服务：CRUD + 确认。
/// 乙供（Self）确认后回写成本；甲供（Owner）只登记、不回写我方成本。
/// </summary>
public class MaterialReceiptService
{
    private readonly AppDbContext _db;
    private readonly CostWriteBackService _writer;

    public MaterialReceiptService(AppDbContext db, CostWriteBackService writer)
    {
        _db = db;
        _writer = writer;
    }

    public Task<List<MaterialReceipt>> ListAsync(int projectId)
        => _db.MaterialReceipts.Where(x => x.ProjectId == projectId).OrderBy(x => x.Code).ToListAsync();

    public async Task<int> SaveAsync(MaterialReceipt mr)
    {
        if (mr.Amount == 0 && mr.Quantity != 0 && mr.UnitPrice != 0)
            mr.Amount = decimal.Round(mr.Quantity * mr.UnitPrice, 2);
        if (mr.Id == 0) _db.MaterialReceipts.Add(mr);
        else _db.MaterialReceipts.Update(mr);
        await _db.SaveChangesAsync();
        return mr.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var mr = await _db.MaterialReceipts.FindAsync(id);
        if (mr is null) return;
        _db.MaterialReceipts.Remove(mr);
        await _db.SaveChangesAsync();
    }

    /// <summary>确认。乙供回写成本并返回 true；甲供不回写返回 false。</summary>
    public async Task<bool> ConfirmAsync(int id)
    {
        var mr = await _db.MaterialReceipts.FirstOrDefaultAsync(x => x.Id == id);
        if (mr is null) throw new InvalidOperationException("材料收料单不存在");
        if (mr.Status == DocStatus.Confirmed) return false;
        mr.Status = DocStatus.Confirmed;
        await _db.SaveChangesAsync();

        if (mr.Source == MaterialSource.Owner) return false; // 甲供不计我方成本

        return await _writer.AppendAsync(mr.ProjectId, mr.CostCategoryId, mr.ContractId,
            VoucherType.MaterialReceipt, mr.Code, mr.Amount, mr.ReceiptDate, $"材料 {mr.MaterialName}");
    }
}
