using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>资金台账行：按合同列出 合同额 / 累计收(付) / 比例 / 未收(付)。</summary>
public record FundLedgerRow(string ContractCode, string Title, string PartyName,
    decimal ContractAmount, decimal Cumulative, decimal Ratio, decimal Outstanding);

/// <summary>
/// 收付款服务（资金流台账）。
/// 定位（2026-10-09 用户确认）：只做**记录 + 累计**——收款/付款的时间、金额、累计、比例、未收(付)。
/// 不做抵扣试算、不做审批、不写成本脊柱（铁律）。
/// </summary>
public class PaymentService
{
    private readonly AppDbContext _db;
    public PaymentService(AppDbContext db) => _db = db;

    /// <summary>按项目列出收付款（经合同归属项目过滤）。</summary>
    public Task<List<Payment>> ListAsync(int projectId)
        => _db.Payments
            .Include(p => p.Party)
            .Where(p => p.Contract != null && p.Contract.ProjectId == projectId)
            .OrderBy(p => p.Date)
            .ToListAsync();

    public async Task<int> SaveAsync(Payment p)
    {
        if (p.Id == 0) _db.Payments.Add(p);
        else _db.Payments.Update(p);
        await _db.SaveChangesAsync();
        return p.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var p = await _db.Payments.FindAsync(id);
        if (p is null) return;
        _db.Payments.Remove(p);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 资金台账：按合同汇总 合同额 / 累计收(付)额 / 比例(=累计/合同额) / 未收(付)(=合同额−累计)。
    /// direction=Receive 为收款台账（对甲方），Pay 为付款台账（对下游）。
    /// </summary>
    public async Task<List<FundLedgerRow>> FundLedgerAsync(int projectId, PaymentDirection direction)
    {
        var contracts = await _db.Contracts
            .Where(c => c.ProjectId == projectId).OrderBy(c => c.Code).ToListAsync();
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        var sums = await _db.Payments
            .Where(p => p.Contract != null && p.Contract.ProjectId == projectId && p.Direction == direction)
            .GroupBy(p => p.ContractId!.Value)
            .Select(g => new { ContractId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync();

        return contracts.Select(c =>
        {
            var cumulative = sums.FirstOrDefault(x => x.ContractId == c.Id)?.Sum ?? 0m;
            var ratio = c.Amount == 0 ? 0m : cumulative / c.Amount;
            return new FundLedgerRow(
                c.Code, c.Title ?? "", parties.GetValueOrDefault(c.PartyId, ""),
                c.Amount, cumulative, ratio, c.Amount - cumulative);
        }).ToList();
    }
}
