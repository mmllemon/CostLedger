using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 收付款服务（资金流）：CRUD + 预付款余额 + 付款试算。
/// 不写成本脊柱（资金流 ≠ 成本发生；铁律）。
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

    /// <summary>某合同的预付款未抵扣余额 = Σ(预付款) − Σ(已抵扣)。</summary>
    public async Task<decimal> AdvanceOutstandingAsync(int contractId)
    {
        var rows = await _db.Payments
            .Where(p => p.ContractId == contractId && p.Direction == PaymentDirection.Pay)
            .ToListAsync();
        var advancePaid = rows.Where(p => p.Type == PaymentType.Advance).Sum(p => p.Amount);
        var deducted = rows.Sum(p => p.AdvanceDeduction ?? 0m);
        return advancePaid - deducted;
    }

    /// <summary>按合同参数试算本期净付（合同为 null 时按无质保/无预付处理）。</summary>
    public async Task<PaymentBreakdown> ComputeAsync(int? contractId, decimal gross)
    {
        if (contractId is null)
            return PaymentCalculator.Compute(gross, 0m, 0m, 0m, null);

        var c = await _db.Contracts.FindAsync(contractId.Value);
        if (c is null)
            return PaymentCalculator.Compute(gross, 0m, 0m, 0m, null);

        var outstanding = await AdvanceOutstandingAsync(c.Id);
        return PaymentCalculator.Compute(gross, c.RetentionRate, c.AdvanceRate, outstanding, c.AdvanceDeductionMethod);
    }
}
