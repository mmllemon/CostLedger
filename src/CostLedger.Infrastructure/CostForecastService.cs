using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace CostLedger.Infrastructure;

/// <summary>
/// 成本预测版本与变化事项服务。
/// 边界：输出的是成本人员的管理预测，不是财务结论；预测版本冻结后不可改写，修订产生新版本。
/// </summary>
public sealed class CostForecastService
{
    private readonly AppDbContext _db;

    public CostForecastService(AppDbContext db) => _db = db;

    /// <summary>新建预测版本（草稿）。版本号在项目内递增，历史版本不被覆盖。</summary>
    public async Task<CostForecastVersion> CreateVersionAsync(
        int projectId, DateOnly forecastDate, ForecastConfidence confidence,
        string? basis = null, string? note = null, CancellationToken ct = default)
    {
        if (!await _db.Projects.AnyAsync(x => x.Id == projectId, ct))
            throw new InvalidOperationException($"未找到项目 {projectId}。");

        var maxVersion = await _db.CostForecastVersions
            .Where(x => x.ProjectId == projectId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(ct) ?? 0;

        var version = new CostForecastVersion
        {
            ProjectId = projectId,
            VersionNumber = maxVersion + 1,
            ForecastDate = forecastDate,
            Confidence = confidence,
            Basis = basis,
            Note = note,
            Status = ForecastVersionStatus.Draft
        };
        _db.CostForecastVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        return version;
    }

    /// <summary>
    /// 录入或修订某控制项的预测行。已知金额由系统按分配关系计算，不接受手填，
    /// 避免"改了结果却没有对应的外部金额依据"。
    /// </summary>
    public async Task<CostForecastLine> UpsertLineAsync(
        int versionId, int costControlItemId, decimal targetAmount, decimal remainingEstimate,
        decimal riskAdjustment, CancellationToken ct = default)
    {
        var version = await GetEditableVersionAsync(versionId, ct);
        var item = await _db.CostControlItems.SingleOrDefaultAsync(x => x.Id == costControlItemId, ct)
            ?? throw new InvalidOperationException($"未找到成本控制项 {costControlItemId}。");
        if (item.ProjectId != version.ProjectId)
            throw new InvalidOperationException("成本控制项必须与预测版本属于同一项目。");

        var known = await KnownAmountAsync(version.ProjectId, costControlItemId, ct);
        var cap = await PrecisionCapAsync(version.ProjectId, costControlItemId, ct);

        var line = await _db.CostForecastLines.SingleOrDefaultAsync(
            x => x.CostForecastVersionId == versionId && x.CostControlItemId == costControlItemId, ct);
        if (line == null)
        {
            line = new CostForecastLine
            {
                CostForecastVersionId = versionId,
                CostControlItemId = costControlItemId
            };
            _db.CostForecastLines.Add(line);
        }

        line.TargetAmount = targetAmount;
        line.KnownAmount = known;
        line.RemainingEstimate = remainingEstimate;
        line.RiskAdjustment = riskAdjustment;
        line.PrecisionCap = cap;

        await _db.SaveChangesAsync(ct);
        return line;
    }

    /// <summary>
    /// 冻结版本：必填编制假设，并按数据实际情况压低可信度上限。
    /// 冻结后成为历史对比基线，任何修订都必须开新版本。
    /// </summary>
    public async Task<CostForecastVersion> FreezeAsync(int versionId, CancellationToken ct = default)
    {
        var version = await GetEditableVersionAsync(versionId, ct);
        var lines = await _db.CostForecastLines
            .Where(x => x.CostForecastVersionId == versionId)
            .ToListAsync(ct);
        if (lines.Count == 0)
            throw new InvalidOperationException("预测版本没有任何控制项预测行，不能冻结。");
        if (string.IsNullOrWhiteSpace(version.Basis))
            throw new InvalidOperationException("冻结预测版本必须写明编制假设（价格水平、剩余工程量依据、未确认事项处理方式）。");

        var economy = await new EconomicFactsService(_db).SummarizeProjectAsync(version.ProjectId, ct);
        if (economy.UnallocatedAmount > 0)
        {
            version.Confidence = version.Confidence switch
            {
                ForecastConfidence.High => ForecastConfidence.Medium,
                _ => ForecastConfidence.Low
            };
        }

        version.Status = ForecastVersionStatus.Frozen;
        await _db.SaveChangesAsync(ct);
        return version;
    }

    /// <summary>登记变化事项的成本影响；四个金额相互独立，状态必须符合责任边界。</summary>
    public async Task<ChangeItem> RegisterChangeItemAsync(ChangeItem item, CancellationToken ct = default)
    {
        var violation = ForecastRules.FindViolation(item);
        if (violation != null)
            throw new InvalidOperationException(violation);

        if (!await _db.Projects.AnyAsync(x => x.Id == item.ProjectId, ct))
            throw new InvalidOperationException($"未找到项目 {item.ProjectId}。");

        if (item.CostControlItemId is int itemId)
        {
            var item2 = await _db.CostControlItems.SingleOrDefaultAsync(x => x.Id == itemId, ct)
                ?? throw new InvalidOperationException($"未找到成本控制项 {itemId}。");
            if (item2.ProjectId != item.ProjectId)
                throw new InvalidOperationException("成本控制项必须与变化事项属于同一项目。");
        }

        if (item.ContractId is int contractId)
        {
            var contract = await _db.Contracts.SingleOrDefaultAsync(x => x.Id == contractId, ct)
                ?? throw new InvalidOperationException($"未找到合同 {contractId}。");
            if (contract.ProjectId != item.ProjectId)
                throw new InvalidOperationException("合同必须与变化事项属于同一项目。");
        }

        _db.ChangeItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>修订变化事项的金额或状态（事项本身不是正式成果，允许持续更新到确认为止）。</summary>
    public async Task<ChangeItem> UpdateChangeItemAsync(ChangeItem item, CancellationToken ct = default)
    {
        var violation = ForecastRules.FindViolation(item);
        if (violation != null)
            throw new InvalidOperationException(violation);

        var existing = await _db.ChangeItems.SingleOrDefaultAsync(x => x.Id == item.Id, ct)
            ?? throw new InvalidOperationException($"未找到变化事项 {item.Id}。");
        if (existing.ProjectId != item.ProjectId)
            throw new InvalidOperationException("变化事项不允许跨项目改写。");

        _db.Entry(existing).CurrentValues.SetValues(item);
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public Task<List<ChangeItem>> ListChangeItemsAsync(int projectId, CancellationToken ct = default)
        => _db.ChangeItems
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.AsOfDate)
            .ToListAsync(ct);

    /// <summary>取项目最新预测版本（无版本时返回 null）。</summary>
    public async Task<CostForecastVersion?> GetLatestVersionAsync(int projectId, CancellationToken ct = default)
        => await _db.CostForecastVersions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// 预测汇总：目标 / 已知 / 剩余估计 / 风险调整 / 预计最终分栏，
    /// 并给出未归属金额、未纳入预测的变化金额、依赖未确认事项的金额，以及与上一冻结版本的差额。
    /// </summary>
    public async Task<CostForecastSummary?> GetSummaryAsync(
        int projectId, int? versionId = null, CancellationToken ct = default)
    {
        var version = versionId is int id
            ? await _db.CostForecastVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct)
            : await GetLatestVersionAsync(projectId, ct);
        if (version == null) return null;

        var lines = await _db.CostForecastLines
            .AsNoTracking()
            .Where(x => x.CostForecastVersionId == version.Id)
            .Include(x => x.CostControlItem)
            .ToListAsync(ct);

        var items = lines.Select(x => x.CostControlItemId).Distinct().ToList();
        var liveKnown = new Dictionary<int, decimal>();
        var liveCap = new Dictionary<int, ManagementPrecision>();
        foreach (var itemId in items)
        {
            liveKnown[itemId] = await KnownAmountAsync(projectId, itemId, ct);
            liveCap[itemId] = await PrecisionCapAsync(projectId, itemId, ct);
        }

        var changeItems = await _db.ChangeItems
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        var unincluded = changeItems
            .Where(x => x.Status != ChangeItemStatus.NotIncluded && x.IncludedInForecast == null)
            .Sum(x => x.CostOfficerEstimate ?? x.CounterpartySubmitted ?? 0m);
        var pendingConfirmation = changeItems
            .Where(x => x.IncludedInForecast != null
                        && x.Status is ChangeItemStatus.Assessed or ChangeItemStatus.Submitted or ChangeItemStatus.ExternalFeedback)
            .Sum(x => x.IncludedInForecast!.Value);

        var economy = await new EconomicFactsService(_db).SummarizeProjectAsync(projectId, ct);

        // 草稿版本按当前分配重算已知金额；冻结版本保持其当时的快照，不被后来的数据改写。
        var summaries = lines.Select(x =>
        {
            var known = version.Status == ForecastVersionStatus.Draft
                ? liveKnown.GetValueOrDefault(x.CostControlItemId, x.KnownAmount)
                : x.KnownAmount;
            var cap = version.Status == ForecastVersionStatus.Draft
                ? liveCap.GetValueOrDefault(x.CostControlItemId, x.PrecisionCap)
                : x.PrecisionCap;
            var estimatedFinal = known + x.RemainingEstimate + x.RiskAdjustment;
            return new CostForecastLineSummary(
                x.CostControlItemId,
                x.CostControlItem!.Code,
                x.CostControlItem.Name,
                x.TargetAmount,
                known,
                x.RemainingEstimate,
                x.RiskAdjustment,
                estimatedFinal,
                estimatedFinal - x.TargetAmount,
                cap,
                changeItems.Where(c => c.CostControlItemId == x.CostControlItemId
                                       && c.Status != ChangeItemStatus.NotIncluded
                                       && c.IncludedInForecast == null)
                    .Sum(c => c.CostOfficerEstimate ?? c.CounterpartySubmitted ?? 0m),
                changeItems.Where(c => c.CostControlItemId == x.CostControlItemId
                                       && c.IncludedInForecast != null
                                       && c.Status is ChangeItemStatus.Assessed or ChangeItemStatus.Submitted or ChangeItemStatus.ExternalFeedback)
                    .Sum(c => c.IncludedInForecast!.Value));
        }).OrderBy(x => x.Code).ToList();

        var targetTotal = summaries.Sum(x => x.TargetAmount);
        var knownTotal = summaries.Sum(x => x.KnownAmount);
        var remainingTotal = summaries.Sum(x => x.RemainingEstimate);
        var riskTotal = summaries.Sum(x => x.RiskAdjustment);
        var estimatedTotal = summaries.Sum(x => x.EstimatedFinal);

        var previous = await _db.CostForecastLines
            .AsNoTracking()
            .Where(x => x.CostForecastVersion!.ProjectId == projectId
                        && x.CostForecastVersion!.Status == ForecastVersionStatus.Frozen
                        && x.CostForecastVersion!.VersionNumber < version.VersionNumber)
            .GroupBy(x => x.CostForecastVersionId)
            .Select(g => new { Version = g.Max(x => x.CostForecastVersionId), Sum = g.Sum(x => x.KnownAmount + x.RemainingEstimate + x.RiskAdjustment) })
            .ToListAsync(ct);
        var prevRow = previous
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Version, x.Sum })
            .FirstOrDefault();

        var allocatable = economy.AllocatableAmount;
        var allocatedToItems = summaries.Sum(x => x.KnownAmount);
        var activeItems = await _db.CostControlItems.CountAsync(x => x.ProjectId == projectId && x.IsActive, ct);

        return new CostForecastSummary(
            version.Id, version.VersionNumber, version.ForecastDate, version.Confidence,
            version.Status, version.Basis,
            targetTotal, knownTotal, remainingTotal, riskTotal, estimatedTotal,
            estimatedTotal - targetTotal,
            economy.UnallocatedAmount,
            unincluded,
            pendingConfirmation,
            activeItems,
            summaries.Count,
            allocatable == 0 ? 0m : Math.Min(allocatedToItems / allocatable, 1m),
            prevRow?.Version,
            prevRow?.Sum ?? 0m,
            estimatedTotal - (prevRow?.Sum ?? 0m),
            summaries);
    }

    private async Task<CostForecastVersion> GetEditableVersionAsync(int versionId, CancellationToken ct)
    {
        var version = await _db.CostForecastVersions
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new InvalidOperationException($"未找到预测版本 {versionId}。");
        if (version.Status == ForecastVersionStatus.Frozen)
            throw new InvalidOperationException($"预测版本 v{version.VersionNumber} 已冻结，不能被改写；请新建版本再修订。");
        return version;
    }

    private Task<decimal> KnownAmountAsync(int projectId, int costControlItemId, CancellationToken ct)
        => _db.ManagementAllocations
            .Where(x => x.CostControlItemId == costControlItemId
                        && x.ExternalAmountFact!.ProjectId == projectId
                        && AllocatableSegments.Values.Contains(x.ExternalAmountFact!.Segment))
            .SumAsync(x => x.Amount, ct);

    private async Task<ManagementPrecision> PrecisionCapAsync(int projectId, int costControlItemId, CancellationToken ct)
    {
        var precisions = await _db.ManagementAllocations
            .Where(x => x.CostControlItemId == costControlItemId
                        && x.ExternalAmountFact!.ProjectId == projectId
                        && AllocatableSegments.Values.Contains(x.ExternalAmountFact!.Segment))
            .Select(x => x.Precision)
            .ToListAsync(ct);
        return precisions.Count == 0 ? ManagementPrecision.Project : precisions.Min();
    }
}
