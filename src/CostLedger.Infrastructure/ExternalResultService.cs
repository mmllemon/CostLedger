using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 外部成果结果服务（P4）：登记并汇总外部造价成果的关键金额结果。
/// 只记录报送额与确认额及其来源，不生成成果文件，不保存计价依据/计算底稿。
/// </summary>
public sealed class ExternalResultService
{
    private readonly AppDbContext _db;
    public ExternalResultService(AppDbContext db) => _db = db;

    /// <summary>登记一条外部成果结果。</summary>
    public async Task<ExternalResultRecord> RegisterAsync(ExternalResultRecord record, CancellationToken ct = default)
    {
        var violation = ExternalResultRules.FindViolation(record);
        if (violation != null)
            throw new InvalidOperationException(violation);

        if (!await _db.Projects.AnyAsync(x => x.Id == record.ProjectId, ct))
            throw new InvalidOperationException($"未找到项目 {record.ProjectId}。");

        if (record.CostControlItemId is int itemId)
        {
            var item = await _db.CostControlItems.SingleOrDefaultAsync(x => x.Id == itemId, ct)
                ?? throw new InvalidOperationException($"未找到成本控制项 {itemId}。");
            if (item.ProjectId != record.ProjectId)
                throw new InvalidOperationException("成本控制项必须与外部成果结果属于同一项目。");
        }

        _db.ExternalResultRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public Task<List<ExternalResultRecord>> ListAsync(int projectId, CancellationToken ct = default)
        => _db.ExternalResultRecords
            .AsNoTracking()
            .Include(x => x.CostControlItem)
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.AsOfDate)
            .ToListAsync(ct);

    /// <summary>
    /// 汇总外部成果结果（收入侧）：报送合计、确认合计、差额（确认−报送）。
    /// 差额为负表示我方报送高于对方确认，是需要关注的风险方向，但不是财务结论。
    /// </summary>
    public async Task<ExternalResultSummary> SummarizeAsync(int projectId, CancellationToken ct = default)
    {
        var rows = await ListAsync(projectId, ct);
        var lines = rows.Select(r => new ExternalResultLine(
            r.Id, r.Period, r.Type, r.SubmittedAmount, r.ConfirmedAmount, r.Variance,
            r.AsOfDate, r.Source, r.CostControlItem?.Name)).ToList();

        return new ExternalResultSummary(
            rows.Sum(r => r.SubmittedAmount),
            rows.Sum(r => r.ConfirmedAmount),
            rows.Sum(r => r.Variance),
            rows.Count == 0 ? null : rows.Max(r => r.AsOfDate),
            lines);
    }

    /// <summary>
    /// 结算结果对目标/预测的差异（P4）：把外部成果结果与项目的收入预测、成本预测并列，
    /// 让成本人员看清"送审/审定 vs 预计收入 vs 预计最终成本"三者的口径差异。
    /// 三者口径不同、**不加总**；这里只并列与相减，不合成利润结论。
    /// </summary>
    public async Task<SettlementVarianceSummary> SummarizeVarianceAsync(int projectId, CancellationToken ct = default)
    {
        var result = await SummarizeAsync(projectId, ct);
        var economy = await new EconomicFactsService(_db).SummarizeProjectAsync(projectId, ct);
        var forecast = await new CostForecastService(_db).GetSummaryAsync(projectId, ct: ct);

        // 审定结算视为收入侧最可信的外部结果；没有审定时退而用送审。
        var approved = (await ListAsync(projectId, ct))
            .Where(r => r.Type == ResultRecordType.ApprovedSettlement).Sum(r => r.ConfirmedAmount);
        var audited = result.ConfirmedTotal;
        var effectiveRevenue = approved > 0 ? approved : audited;

        return new SettlementVarianceSummary(
            SubmittedRevenue: result.SubmittedTotal,
            ConfirmedRevenue: result.ConfirmedTotal,
            ExpectedIncome: economy.ExpectedIncome,
            RevenueVarianceVsSubmitted: effectiveRevenue - result.SubmittedTotal,
            RevenueVarianceVsExpected: effectiveRevenue - economy.ExpectedIncome,
            EstimatedFinalCost: forecast?.EstimatedFinalTotal,
            TargetCost: forecast?.TargetTotal,
            HasForecast: forecast != null,
            LatestAsOfDate: result.LatestAsOfDate);
    }

    /// <summary>
    /// 在某个成果结果下登记一行结算清单（可选）。对齐控制项可空——不填即为未对齐，允许。
    /// </summary>
    public async Task<SettlementLineItem> AddLineItemAsync(
        int externalResultRecordId, string name, decimal amount, int? costControlItemId = null,
        string? note = null, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("清单行名称必填。");
        if (amount < 0)
            throw new InvalidOperationException("清单行金额不能为负。");

        var record = await _db.ExternalResultRecords.SingleOrDefaultAsync(x => x.Id == externalResultRecordId, ct)
            ?? throw new InvalidOperationException($"未找到外部成果结果 {externalResultRecordId}。");

        if (costControlItemId is int itemId)
        {
            var item = await _db.CostControlItems.SingleOrDefaultAsync(x => x.Id == itemId, ct)
                ?? throw new InvalidOperationException($"未找到成本控制项 {itemId}。");
            if (item.ProjectId != record.ProjectId)
                throw new InvalidOperationException("对齐的成本控制项必须与成果结果属于同一项目。");
        }

        var line = new SettlementLineItem
        {
            ExternalResultRecordId = record.Id,
            Name = name,
            Amount = amount,
            CostControlItemId = costControlItemId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
        _db.SettlementLineItems.Add(line);
        await _db.SaveChangesAsync(ct);
        return line;
    }

    public Task<List<SettlementLineItem>> ListLineItemsAsync(int externalResultRecordId, CancellationToken ct = default)
        => _db.SettlementLineItems
            .AsNoTracking()
            .Include(x => x.CostControlItem)
            .Where(x => x.ExternalResultRecordId == externalResultRecordId)
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct);

    /// <summary>按项目汇总全部结算清单行的对齐情况（对齐金额 / 未对齐金额 / 对齐率）。</summary>
    public async Task<SettlementAlignmentSummary> SummarizeAlignmentAsync(int projectId, CancellationToken ct = default)
    {
        var lines = await _db.SettlementLineItems
            .AsNoTracking()
            .Where(x => x.ExternalResultRecord!.ProjectId == projectId)
            .ToListAsync(ct);

        var aligned = lines.Where(x => x.CostControlItemId.HasValue).Sum(x => x.Amount);
        var total = lines.Sum(x => x.Amount);
        return new SettlementAlignmentSummary(
            aligned,
            total - aligned,
            total,
            lines.Count,
            lines.Count(x => x.CostControlItemId.HasValue));
    }
}
