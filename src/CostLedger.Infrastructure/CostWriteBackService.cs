using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 成本回写引擎——成本脊柱的**唯一写入入口**（凭证式、只追加、幂等）。
/// 任何业务单据（合同/计量/变更令/材料收料…）都不直接改成本，
/// 而是经此追加一条 CostEntry，由 CostSpineService 聚合得出六类口径。
/// 幂等键 = (SourceVoucherType, SourceVoucherRef)，重复确认不会重复记账。
/// </summary>
public class CostWriteBackService
{
    private readonly AppDbContext _db;
    public CostWriteBackService(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(VoucherType type, string refNo, CancellationToken ct = default)
        => _db.CostEntries.AnyAsync(e => e.SourceVoucherType == type && e.SourceVoucherRef == refNo, ct);

    /// <summary>追加一条成本分录；若同来源已存在则跳过（幂等）。返回是否实际写入。</summary>
    public async Task<bool> AppendAsync(
        int projectId, int? costCategoryId, int? contractId,
        VoucherType type, string refNo, decimal amount, DateOnly entryDate,
        string? note = null, int? wbsItemId = null, CancellationToken ct = default)
    {
        if (await ExistsAsync(type, refNo, ct)) return false;

        // 未指定科目时，回退到"合同挂的成本大类"，保证实际成本能落到大类
        var cat = costCategoryId;
        if (cat is null && contractId is not null)
        {
            var contract = await _db.Contracts.FindAsync(new object?[] { contractId.Value }, ct);
            cat = contract?.CostCategoryId;
        }

        _db.CostEntries.Add(new CostEntry
        {
            ProjectId = projectId,
            CostCategoryId = cat,
            ContractId = contractId,
            WbsItemId = wbsItemId,
            SourceVoucherType = type,
            SourceVoucherRef = refNo,
            Amount = amount,
            EntryDate = entryDate,
            Note = note
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>冲销：追加一条反向分录（不删除、不修改原分录），幂等键为 refNo+"-REV"。</summary>
    public async Task<bool> ReverseAsync(
        int projectId, int? costCategoryId, int? contractId,
        VoucherType type, string refNo, decimal amount, DateOnly entryDate,
        string? note = null, int? wbsItemId = null, CancellationToken ct = default)
        => await AppendAsync(projectId, costCategoryId, contractId, type, refNo + "-REV", -amount, entryDate,
            note ?? $"冲销 {refNo}", wbsItemId, ct);
}
