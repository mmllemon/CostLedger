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
}
