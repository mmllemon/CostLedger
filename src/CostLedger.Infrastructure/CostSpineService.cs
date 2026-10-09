using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// 成本脊柱服务实现：按 (Project, CostCategory) 维度聚合六类金额口径。
/// 目标成本来自合同明细（ContractLine.Amount）；已发生来自 CostEntry 汇总。
/// 任何偏差（超支）天然由数据得出，不修改任何源单据——符合凭证式记账与回写引擎设计。
/// </summary>
public class CostSpineService : ICostSpineService
{
    private readonly AppDbContext _db;
    public CostSpineService(AppDbContext db) => _db = db;

    public async Task<List<CostSpineLine>> GetSpineAsync(int projectId, CancellationToken ct = default)
    {
        var target = await (
            from l in _db.ContractLines
            join c in _db.Contracts on l.ContractId equals c.Id
            where c.ProjectId == projectId && l.CostCategoryId != null
            group l by l.CostCategoryId!.Value into g
            select new { Cat = g.Key, Sum = g.Sum(l => l.Amount) }
        ).ToListAsync(ct);

        var incurred = await _db.CostEntries
            .Where(e => e.ProjectId == projectId && e.CostCategoryId != null)
            .GroupBy(e => e.CostCategoryId!.Value)
            .Select(g => new { Cat = g.Key, Sum = g.Sum(e => e.Amount) })
            .ToListAsync(ct);

        var catIds = target.Select(x => x.Cat).Concat(incurred.Select(x => x.Cat)).Distinct().ToList();
        var cats = await _db.CostCategories
            .Where(c => catIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c, ct);

        return catIds.Select(id =>
        {
            var cat = cats[id];
            var t = target.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m;
            var i = incurred.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m;
            return new CostSpineLine
            {
                CostCategoryId = id,
                CostCategoryName = cat.Name,
                CategoryType = cat.Type,
                Target = t,
                Committed = t,   // M1：承诺额以合同明细映射（与目标同源）
                Incurred = i
            };
        }).OrderBy(x => x.CostCategoryName).ToList();
    }
}
