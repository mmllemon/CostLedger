using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>一个项目的公司级经营行。每个字段都等于该项目页的同一口径，不做二次计算。</summary>
public sealed record CompanyProjectRow(
    int ProjectId,
    string Code,
    string Name,
    decimal ExpectedIncome,
    decimal KnownExpenditure,
    decimal Received,
    decimal Paid,
    decimal FundingGap,
    decimal? EstimatedFinal,
    decimal? TargetTotal,
    decimal? Variance,
    decimal CoverageRate,
    decimal UnallocatedAmount,
    DateOnly? AsOfDate,
    int? ForecastVersionNumber,
    ForecastVersionStatus? ForecastStatus);

/// <summary>
/// 公司级经营汇总：把每个项目的经济摘要逐列相加得到公司合计。
/// 关键约束：合计 = 各项目同口径相加，不重新计算；没有预测版本的项目"预计最终"为 null，
/// 合计只累加有预测的项目，并在 MissingForecastCount 里如实报告。
/// </summary>
public sealed record CompanySummary(
    int ProjectCount,
    int MissingForecastCount,
    decimal ExpectedIncome,
    decimal KnownExpenditure,
    decimal Received,
    decimal Paid,
    decimal FundingGap,
    decimal EstimatedFinal,
    decimal TargetTotal,
    decimal Variance,
    decimal UnallocatedAmount,
    decimal CoverageRate,
    IReadOnlyList<CompanyProjectRow> Projects);

/// <summary>公司级经营驾驶舱的取数服务。口径与项目页完全一致，只做跨项目汇总。</summary>
public sealed class CompanyService
{
    private readonly AppDbContext _db;
    private readonly EconomicFactsService _facts;
    private readonly CostForecastService _forecast;

    public CompanyService(AppDbContext db)
    {
        _db = db;
        _facts = new EconomicFactsService(db);
        _forecast = new CostForecastService(db);
    }

    public async Task<CompanySummary> SummarizeAsync(CancellationToken ct = default)
    {
        var projects = await _db.Projects.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);
        var rows = new List<CompanyProjectRow>(projects.Count);

        foreach (var p in projects)
        {
            var e = await _facts.SummarizeProjectAsync(p.Id, ct);
            var f = await _forecast.GetSummaryAsync(p.Id, ct: ct);
            rows.Add(new CompanyProjectRow(
                p.Id, p.Code, p.Name,
                e.ExpectedIncome, e.KnownExpenditure, e.Received, e.Paid, e.FundingGap,
                f?.EstimatedFinalTotal, f?.TargetTotal, f?.VarianceTotal,
                e.CoverageRate, e.UnallocatedAmount, e.LatestAsOfDate,
                f?.VersionNumber, f?.Status));
        }

        // 合计只累加"有值"的项（预计最终/目标/偏差仅统计有预测版本的项目），其余逐列相加。
        var withForecast = rows.Where(r => r.EstimatedFinal.HasValue).ToList();
        var estimatedFinal = withForecast.Sum(r => r.EstimatedFinal!.Value);
        var targetTotal = withForecast.Sum(r => r.TargetTotal ?? 0m);
        var variance = withForecast.Sum(r => r.Variance ?? 0m);

        var totalAllocatable = await TotalAllocatableAsync(ct);
        var totalAllocated = await TotalAllocatedAsync(ct);
        var coverage = totalAllocatable == 0 ? 0m : Math.Min(totalAllocated / totalAllocatable, 1m);

        return new CompanySummary(
            rows.Count,
            rows.Count - withForecast.Count,
            rows.Sum(r => r.ExpectedIncome),
            rows.Sum(r => r.KnownExpenditure),
            rows.Sum(r => r.Received),
            rows.Sum(r => r.Paid),
            rows.Sum(r => r.FundingGap),
            estimatedFinal,
            targetTotal,
            variance,
            rows.Sum(r => r.UnallocatedAmount),
            coverage,
            rows);
    }

    private Task<decimal> TotalAllocatableAsync(CancellationToken ct) =>
        _db.ExternalAmountFacts.AsNoTracking()
            .Where(x => AllocatableSegments.Values.Contains(x.Segment))
            .SumAsync(x => x.Amount, ct);

    private Task<decimal> TotalAllocatedAsync(CancellationToken ct) =>
        _db.ManagementAllocations.AsNoTracking()
            .Where(x => AllocatableSegments.Values.Contains(x.ExternalAmountFact!.Segment))
            .SumAsync(x => x.Amount, ct);
}
