using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 手工成本录入：人工 / 机械 / 现场经费等**非单据成本**的登记入口。
/// 仍走凭证式过账（CostWriteBackService），只追加、可冲销，天然进入「已发生」口径，
/// 与材料收料、分包等单据来源成本同源归集，是"已发生成本"补全覆盖的关键一环。
/// </summary>
public class ManualCostService
{
    private readonly AppDbContext _db;
    private readonly CostWriteBackService _writer;

    public ManualCostService(AppDbContext db, CostWriteBackService writer)
    {
        _db = db;
        _writer = writer;
    }

    /// <summary>登记一笔手工成本（凭证式追加）。ref 自动唯一，每次调用都是一条独立分录。返回是否写入成功。</summary>
    public Task<bool> RecordAsync(int projectId, int? costCategoryId, int? wbsItemId, int? contractId,
        decimal amount, DateOnly entryDate, string? note = null, CancellationToken ct = default)
    {
        var refNo = "M" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return _writer.AppendAsync(projectId, costCategoryId, contractId, VoucherType.Manual,
            refNo, amount, entryDate, note, wbsItemId: wbsItemId, ct: ct);
    }

    /// <summary>冲销一笔手工成本（追加反向分录，不删除原分录，保留审计轨迹）。</summary>
    public async Task<bool> ReverseAsync(int entryId, CancellationToken ct = default)
    {
        var e = await _db.CostEntries
            .FirstOrDefaultAsync(x => x.Id == entryId && x.SourceVoucherType == VoucherType.Manual, ct);
        if (e is null) return false;
        return await _writer.ReverseAsync(e.ProjectId, e.CostCategoryId, e.ContractId,
            VoucherType.Manual, e.SourceVoucherRef, e.Amount, e.EntryDate, "冲销手工成本",
            wbsItemId: e.WbsItemId, ct: ct);
    }

    public Task<List<CostEntry>> ListAsync(int projectId, CancellationToken ct = default)
        => _db.CostEntries
            .Where(x => x.ProjectId == projectId && x.SourceVoucherType == VoucherType.Manual)
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
}
