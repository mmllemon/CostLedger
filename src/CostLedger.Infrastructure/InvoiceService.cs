using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 发票税务台账服务：CRUD + 价税分离计算 + 进项税汇总。
/// 不写成本脊柱（发票是税务/资金流的勾稽凭证，非成本发生）。
/// </summary>
public class InvoiceService
{
    private readonly AppDbContext _db;
    public InvoiceService(AppDbContext db) => _db = db;

    public Task<List<Invoice>> ListAsync(int projectId)
        => _db.Invoices.Where(x => x.ProjectId == projectId).OrderBy(x => x.InvoiceDate).ToListAsync();

    public async Task<int> SaveAsync(Invoice inv)
    {
        // 价税分离：给定不含税+税率则补税额与价税合计；给定价税合计+税额则补不含税
        if (inv.TaxAmount == 0 && inv.NetAmount != 0 && inv.TaxRate != 0)
            inv.TaxAmount = decimal.Round(inv.NetAmount * inv.TaxRate, 2);
        if (inv.TotalAmount == 0)
            inv.TotalAmount = inv.NetAmount + inv.TaxAmount;
        if (inv.NetAmount == 0 && inv.TotalAmount != 0)
            inv.NetAmount = inv.TotalAmount - inv.TaxAmount;

        if (inv.Id == 0) _db.Invoices.Add(inv);
        else _db.Invoices.Update(inv);
        await _db.SaveChangesAsync();
        return inv.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var inv = await _db.Invoices.FindAsync(id);
        if (inv is null) return;
        _db.Invoices.Remove(inv);
        await _db.SaveChangesAsync();
    }

    /// <summary>进项税可抵扣税额合计（供进项税台账）。</summary>
    public Task<decimal> InputTaxTotalAsync(int projectId)
        => _db.Invoices
            .Where(x => x.ProjectId == projectId && x.Direction == InvoiceDirection.Input)
            .SumAsync(x => x.TaxAmount);
}
