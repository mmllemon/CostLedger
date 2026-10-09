using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// 成本脊柱服务实现：按 (Project, CostCategory) 维度聚合金额口径。
/// 目标成本 = 合同明细（ContractLine.Amount）+ 已确认变更令调整（ChangeOrder.Amount，带符号）。
/// 已发生 = CostEntry 汇总（凭证式回写）。
/// 任何偏差（超支）天然由数据得出，不修改任何源单据——符合凭证式记账与回写引擎设计。
/// </summary>
public class CostSpineService : ICostSpineService
{
    private readonly AppDbContext _db;
    public CostSpineService(AppDbContext db) => _db = db;

    /// <summary>目标基数聚合（科目, 金额）。</summary>
    private sealed record TargetAgg(int Cat, decimal Sum);

    public async Task<List<CostSpineLine>> GetSpineAsync(int projectId, CancellationToken ct = default)
    {
        // 目标基数：优先用工程量清单（BOQ，按科目汇总）；项目无清单行时回退到合同明细。
        var baseTarget = new List<TargetAgg>();
        if (await _db.BoqLines.AnyAsync(b => b.ProjectId == projectId, ct))
        {
            baseTarget = (await _db.BoqLines
                .Where(b => b.ProjectId == projectId && b.CostCategoryId != null)
                .GroupBy(b => b.CostCategoryId!.Value)
                .Select(g => new { Cat = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync(ct))
                .Select(x => new TargetAgg(x.Cat, x.Sum)).ToList();
        }
        else
        {
            baseTarget = (await (
                from l in _db.ContractLines
                join c in _db.Contracts on l.ContractId equals c.Id
                where c.ProjectId == projectId && l.CostCategoryId != null
                group l by l.CostCategoryId!.Value into g
                select new { Cat = g.Key, Sum = g.Sum(l => l.Amount) }
            ).ToListAsync(ct)).Select(x => new TargetAgg(x.Cat, x.Sum)).ToList();
        }

        // 目标调整：已确认变更令按科目汇总（带符号）
        var adjust = await _db.ChangeOrders
            .Where(x => x.ProjectId == projectId && x.Status == DocStatus.Confirmed && x.CostCategoryId != null)
            .GroupBy(x => x.CostCategoryId!.Value)
            .Select(g => new { Cat = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var incurred = await _db.CostEntries
            .Where(e => e.ProjectId == projectId && e.CostCategoryId != null)
            .GroupBy(e => e.CostCategoryId!.Value)
            .Select(g => new { Cat = g.Key, Sum = g.Sum(e => e.Amount) })
            .ToListAsync(ct);

        // 已完未结：已签认计量明细（按科目）− 已按"计量来源"回写的成本
        var wdnsRaw = await (
            from ml in _db.MeasurementLines
            join m in _db.Measurements on ml.MeasurementId equals m.Id
            where m.ProjectId == projectId && m.IsSigned && ml.CostCategoryId != null
            group ml by ml.CostCategoryId!.Value into g
            select new { Cat = g.Key, Sum = g.Sum(x => x.Amount) }
        ).ToListAsync(ct);

        var postedFromMeasurement = await _db.CostEntries
            .Where(e => e.ProjectId == projectId && e.CostCategoryId != null && e.SourceVoucherType == VoucherType.Measurement)
            .GroupBy(e => e.CostCategoryId!.Value)
            .Select(g => new { Cat = g.Key, Sum = g.Sum(e => e.Amount) })
            .ToListAsync(ct);

        var catIds = baseTarget.Select(x => x.Cat)
            .Concat(adjust.Select(x => x.Cat))
            .Concat(incurred.Select(x => x.Cat))
            .Concat(wdnsRaw.Select(x => x.Cat))
            .Distinct().ToList();
        var cats = await _db.CostCategories
            .Where(c => catIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c, ct);

        return catIds.Select(id =>
        {
            var cat = cats[id];
            var t = (baseTarget.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m)
                    + (adjust.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m);
            var i = incurred.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m;
            var wdns = (wdnsRaw.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m)
                       - (postedFromMeasurement.FirstOrDefault(x => x.Cat == id)?.Sum ?? 0m);
            return new CostSpineLine
            {
                CostCategoryId = id,
                CostCategoryName = cat.Name,
                CategoryType = cat.Type,
                Target = t,
                Committed = t,   // M1：承诺额与目标同源（合同明细 + 已确认变更）
                Incurred = i,
                WorkDoneNotSettled = wdns > 0 ? wdns : 0m
            };
        }).OrderBy(x => x.CostCategoryName).ToList();
    }

    /// <summary>
    /// 按部位（WBS）归集：目标（清单）/ 已发生（凭证）。用于定位"哪一段超支"。
    /// 已完未结暂不按部位拆分（计量行暂无部位维度）。
    /// </summary>
    public async Task<List<WbsCostLine>> GetSpineByWbsAsync(int projectId, CancellationToken ct = default)
    {
        var targetByWbs = await _db.BoqLines
            .Where(b => b.ProjectId == projectId && b.WbsItemId != null)
            .GroupBy(b => b.WbsItemId!.Value)
            .Select(g => new { Wbs = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var incurredByWbs = await _db.CostEntries
            .Where(e => e.ProjectId == projectId && e.WbsItemId != null)
            .GroupBy(e => e.WbsItemId!.Value)
            .Select(g => new { Wbs = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var ids = targetByWbs.Select(x => x.Wbs).Concat(incurredByWbs.Select(x => x.Wbs)).Distinct().ToList();
        var wbsItems = await _db.WbsItems.Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w, ct);

        return ids.Select(id =>
        {
            var w = wbsItems[id];
            return new WbsCostLine
            {
                WbsItemId = id,
                WbsCode = w.Code,
                WbsName = w.Name,
                FullPath = w.FullPath,
                Target = targetByWbs.FirstOrDefault(x => x.Wbs == id)?.Sum ?? 0m,
                Incurred = incurredByWbs.FirstOrDefault(x => x.Wbs == id)?.Sum ?? 0m
            };
        }).OrderBy(x => x.WbsCode).ToList();
    }
}
