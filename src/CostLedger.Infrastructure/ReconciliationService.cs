using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>合同 vs 成本：某合同 签约额 与 已发生成本 的对比（Diff&gt;0 表示成本已超合同额）</summary>
public record ContractVsCostRow(string ContractCode, string Title, decimal ContractAmount, decimal Incurred, decimal Diff, bool IsBreach);

/// <summary>三算对比（项目级汇总）：目标 / 实际(已发生) / 结算</summary>
public record ThreeWaySummary(decimal Target, decimal Incurred, decimal Settled, decimal TargetVsIncurred, decimal TargetVsSettled);

/// <summary>按科目的 目标 vs 实际 对比</summary>
public record CategoryTargetActualRow(string Category, decimal Target, decimal Incurred, decimal Variance, bool IsOver);

/// <summary>
/// M3-B 对账引擎：合同vs成本、三算对比（目标/实际/结算）、按科目目标vs实际。
/// 只读聚合，不写回、不参与算账（铁律）。后续可扩展：对上vs对下量差、资金vs应付应收。
/// </summary>
public class ReconciliationService
{
    private readonly AppDbContext _db;
    private readonly ICostSpineService _spine;

    public ReconciliationService(AppDbContext db, ICostSpineService spine)
    {
        _db = db;
        _spine = spine;
    }

    /// <summary>合同 vs 成本：逐合同的签约额与已发生成本对比。</summary>
    public async Task<List<ContractVsCostRow>> ContractVsCostAsync(int projectId)
    {
        var contracts = await _db.Contracts.Where(c => c.ProjectId == projectId).OrderBy(c => c.Code).ToListAsync();
        var incurredByContract = await _db.CostEntries
            .Where(e => e.ProjectId == projectId && e.ContractId != null)
            .GroupBy(e => e.ContractId!.Value)
            .Select(g => new { Contract = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync();

        return contracts.Select(c =>
        {
            var inc = incurredByContract.FirstOrDefault(x => x.Contract == c.Id)?.Sum ?? 0m;
            var diff = inc - c.Amount;
            return new ContractVsCostRow(c.Code, c.Title ?? "", c.Amount, inc, diff, diff > 0);
        }).ToList();
    }

    /// <summary>三算对比（项目级）：目标成本 / 已发生 / 竣工结算。</summary>
    public async Task<ThreeWaySummary> ThreeWaySummaryAsync(int projectId)
    {
        var spine = await _spine.GetSpineAsync(projectId);
        var target = spine.Sum(l => l.Target);
        var incurred = spine.Sum(l => l.Incurred);
        var settled = await _db.Settlements.Where(s => s.ProjectId == projectId).SumAsync(s => s.SettledAmount);
        return new ThreeWaySummary(target, incurred, settled, incurred - target, settled - target);
    }

    /// <summary>按成本科目的 目标 vs 实际 对比。</summary>
    public async Task<List<CategoryTargetActualRow>> CategoryTargetActualAsync(int projectId)
    {
        var spine = await _spine.GetSpineAsync(projectId);
        return spine.Select(l => new CategoryTargetActualRow(
            l.CostCategoryName, l.Target, l.Incurred, l.Variance, l.IsOver)).ToList();
    }
}
