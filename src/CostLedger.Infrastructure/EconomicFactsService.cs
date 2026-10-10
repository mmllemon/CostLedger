using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 管理施工单位成本岗的外部金额事实和控制项分配。
/// 不核验财务正确性；分配不得超过来源金额，来源与控制项必须属于同一项目。
/// </summary>
public sealed class EconomicFactsService
{
    private readonly AppDbContext _db;

    public EconomicFactsService(AppDbContext db) => _db = db;

    /// <summary>登记项目与往来单位的业务关系；板块、角色、金额方向必须符合领域规则。</summary>
    public async Task<ProjectPartyRelationship> RegisterRelationshipAsync(
        ProjectPartyRelationship relationship, CancellationToken ct = default)
    {
        await EnsureProjectAsync(relationship.ProjectId, ct);

        // 角色代码必须是字典「项目业务角色」中已启用的项（内置六个代码由种子保证存在）。
        var knownRoles = await ListRoleCodesAsync(ct);
        if (knownRoles.Count > 0 && !knownRoles.Contains(relationship.RoleCode))
            throw new InvalidOperationException(
                $"角色 {relationship.RoleCode} 不在字典「项目业务角色」的启用项中；请先在后台字典维护中新增该角色。");

        var extra = await ExtraRolesAsync(relationship.Segment, ct);
        var violation = BusinessSegmentRules.FindViolation(
            relationship.RoleCode, relationship.Segment, relationship.Direction, extra);
        if (violation != null)
            throw new InvalidOperationException(violation);

        if (relationship.ContractId is int contractId)
        {
            var contract = await _db.Contracts.SingleOrDefaultAsync(x => x.Id == contractId, ct);
            if (contract == null) throw new InvalidOperationException($"未找到合同 {contractId}。");
            if (contract.ProjectId != relationship.ProjectId)
                throw new InvalidOperationException("合同与业务关系必须属于同一项目。");
        }

        _db.ProjectPartyRelationships.Add(relationship);
        await _db.SaveChangesAsync(ct);
        return relationship;
    }

    /// <summary>
    /// 后台「板块角色授权」：把字典新增的角色增补到某板块（如机械租赁单位 → 材料供应）。
    /// 只能增补角色，不能新增板块或放宽金额方向。
    /// </summary>
    public async Task<SegmentRolePermission> GrantSegmentRoleAsync(
        BusinessSegment segment, string roleCode, string? note = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new InvalidOperationException("必须指定要增补的角色代码。");

        var knownRoles = await ListRoleCodesAsync(ct);
        if (knownRoles.Count > 0 && !knownRoles.Contains(roleCode))
            throw new InvalidOperationException(
                $"角色 {roleCode} 尚未在字典「项目业务角色」中定义，不能授权到板块。");

        var existing = await _db.SegmentRolePermissions.SingleOrDefaultAsync(
            x => x.Segment == segment && x.RoleCode == roleCode, ct);
        if (existing != null) return existing;

        var grant = new SegmentRolePermission { Segment = segment, RoleCode = roleCode, Note = note };
        _db.SegmentRolePermissions.Add(grant);
        await _db.SaveChangesAsync(ct);
        return grant;
    }

    public Task<List<SegmentRolePermission>> ListSegmentRolePermissionsAsync(CancellationToken ct = default)
        => _db.SegmentRolePermissions.AsNoTracking().OrderBy(x => x.Segment).ThenBy(x => x.RoleCode).ToListAsync(ct);

    public async Task RevokeSegmentRoleAsync(int permissionId, CancellationToken ct = default)
    {
        var row = await _db.SegmentRolePermissions.SingleOrDefaultAsync(x => x.Id == permissionId, ct);
        if (row == null) return;
        // 已建立的业务关系不被抹掉：撤权只影响后续登记。
        _db.SegmentRolePermissions.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>可选择的业务角色（字典启用项，含内置代码兜底）。</summary>
    public async Task<List<string>> ListRoleCodesAsync(CancellationToken ct = default)
    {
        var fromLookup = await _db.LookupItems
            .AsNoTracking()
            .Where(i => i.IsEnabled && i.LookupDef!.Code == PartyRoleCodes.RoleLookupCode)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Code)
            .ToListAsync(ct);
        return fromLookup.Count > 0 ? fromLookup : PartyRoleCodes.BuiltIn.ToList();
    }

    /// <summary>该板块在内置矩阵之外被增补授权的角色代码。</summary>
    public async Task<List<string>> ExtraRolesAsync(BusinessSegment segment, CancellationToken ct = default)
        => await _db.SegmentRolePermissions
            .AsNoTracking()
            .Where(x => x.Segment == segment)
            .Select(x => x.RoleCode)
            .ToListAsync(ct);

    /// <summary>登记外部金额事实。金额带来源、截至日期、状态和精度；成本岗不因此核验财务正确性。</summary>
    public async Task<ExternalAmountFact> RegisterFactAsync(
        ExternalAmountFact fact, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fact.Source))
            throw new InvalidOperationException("外部金额事实必须填写来源。");
        if (fact.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(fact.Amount), "外部金额事实不能为负数；减少金额应登记为反向事实或使用对应板块的中性调整。");

        await EnsureProjectAsync(fact.ProjectId, ct);

        if (!BusinessSegmentRules.AllowedDirections(fact.Segment).Contains(fact.Direction))
            throw new InvalidOperationException(
                $"业务板块「{BusinessSegmentRules.DisplayName(fact.Segment)}」的金额方向只能是 {BusinessSegmentRules.AllowedDirectionsText(fact.Segment)}，当前为 {BusinessSegmentRules.DisplayName(fact.Direction)}。");

        if (fact.PartyId is int partyId)
        {
            var relationship = await _db.ProjectPartyRelationships
                .SingleOrDefaultAsync(x =>
                    x.ProjectId == fact.ProjectId && x.PartyId == partyId &&
                    x.Segment == fact.Segment &&
                    (x.ContractId == fact.ContractId || (x.ContractId == null && fact.ContractId == null)), ct);
            if (relationship == null)
                throw new InvalidOperationException(
                    $"项目 {fact.ProjectId} 中该往来单位尚未在「{BusinessSegmentRules.DisplayName(fact.Segment)}」板块建立业务关系，不能直接登记金额。");

            fact.Direction = relationship.Direction;
        }

        _db.ExternalAmountFacts.Add(fact);
        await _db.SaveChangesAsync(ct);
        return fact;
    }

    public async Task<ManagementAllocation> AllocateAsync(
        int factId,
        int costControlItemId,
        decimal amount,
        ManagementPrecision precision,
        AllocationStatus status,
        string? note = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "分配金额必须大于零。");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var fact = await _db.ExternalAmountFacts
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == factId, ct)
            ?? throw new InvalidOperationException($"未找到外部金额事实 {factId}。");
        var item = await _db.CostControlItems
            .SingleOrDefaultAsync(x => x.Id == costControlItemId, ct)
            ?? throw new InvalidOperationException($"未找到成本控制项 {costControlItemId}。");

        if (fact.ProjectId != item.ProjectId)
            throw new InvalidOperationException("外部金额事实与成本控制项必须属于同一项目。");

        var allocated = fact.Allocations.Sum(x => x.Amount);
        if (allocated + amount > fact.Amount)
            throw new InvalidOperationException("分配总额不能超过来源金额。");

        var allocation = new ManagementAllocation
        {
            ExternalAmountFactId = fact.Id,
            CostControlItemId = item.Id,
            Amount = amount,
            Precision = precision,
            Status = status,
            Note = note
        };
        _db.ManagementAllocations.Add(allocation);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return allocation;
    }

    public async Task<List<ManagementAllocation>> ListAllocationsAsync(int projectId, CancellationToken ct = default)
        => await _db.ManagementAllocations
            .Where(x => x.ExternalAmountFact!.ProjectId == projectId)
            .Include(x => x.ExternalAmountFact)
            .Include(x => x.CostControlItem)
            .OrderBy(x => x.CostControlItem!.SortOrder)
            .ThenBy(x => x.ExternalAmountFact!.AsOfDate)
            .ToListAsync(ct);

    /// <summary>
    /// 新建成本控制项。编号与名称必填，编号在项目内唯一。
    /// 校验放在服务层，界面与测试共用同一套规则，避免只在界面里做导致无测试覆盖。
    /// </summary>
    public async Task<CostControlItem> CreateControlItemAsync(
        int projectId, string code, string name, int sortOrder = 0, CancellationToken ct = default)
    {
        await EnsureProjectAsync(projectId, ct);
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        if (code.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("控制项编号与名称必填。");
        if (await _db.CostControlItems.AnyAsync(x => x.ProjectId == projectId && x.Code == code, ct))
            throw new InvalidOperationException($"控制项编号 {code} 在本项目已存在。");

        var item = new CostControlItem { ProjectId = projectId, Code = code, Name = name, SortOrder = sortOrder, IsActive = true };
        _db.CostControlItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>
    /// 按成本控制项汇总已分配金额。同一项目允许粗颗粒过程数据与细颗粒结算数据并存：
    /// 每行保留最粗的来源精度，不提升为清单级。
    /// </summary>
    public async Task<List<CostControlItemSummary>> SummarizeControlItemsAsync(
        int projectId, CancellationToken ct = default)
    {
        var rows = await _db.ManagementAllocations
            .Where(x => x.ExternalAmountFact!.ProjectId == projectId)
            .Include(x => x.ExternalAmountFact)
            .Include(x => x.CostControlItem)
            .ToListAsync(ct);

        return rows.GroupBy(x => new { x.CostControlItemId, x.CostControlItem!.Code, x.CostControlItem.Name, x.CostControlItem.SortOrder })
            .Select(g => new CostControlItemSummary(
                g.Key.CostControlItemId,
                g.Key.Code,
                g.Key.Name,
                g.Sum(x => x.Amount),
                g.Where(x => x.Status == AllocationStatus.Estimated).Sum(x => x.Amount),
                g.Where(x => x.ExternalAmountFact!.Status == ExternalFactStatus.Checked).Sum(x => x.Amount),
                g.Min(x => x.Precision),
                g.Select(x => x.ExternalAmountFactId).Distinct().Count()))
            .OrderBy(x => x.Code)
            .ToList();
    }

    /// <summary>按业务板块汇总外部金额事实，不跨板块相加。</summary>
    public async Task<List<SegmentAmountSummary>> SummarizeSegmentsAsync(int projectId, CancellationToken ct = default)
    {
        var facts = await _db.ExternalAmountFacts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        return facts.GroupBy(x => x.Segment)
            .Select(g => new SegmentAmountSummary(
                g.Key,
                g.Sum(x => x.Amount),
                g.Count(),
                g.Any() ? g.Max(x => x.AsOfDate) : null))
            .OrderBy(x => x.Segment)
            .ToList();
    }

    /// <summary>
    /// 项目经济状态摘要：收入 / 支出 / 已收 / 已付 / 资金压力 / 分配覆盖分开返回。
    /// 已收与已付是外部提供的资金状态，不代表财务确认；资金压力为正表示已知付款额大于已知收款额。
    /// </summary>
    public async Task<ProjectEconomySummary> SummarizeProjectAsync(int projectId, CancellationToken ct = default)
    {
        var facts = await _db.ExternalAmountFacts
            .AsNoTracking()
            .Include(x => x.Allocations)
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        decimal Sum(EconomicDirection direction) => facts.Where(x => x.Direction == direction).Sum(x => x.Amount);
        var allocatableFacts = facts.Where(x => AllocatableSegments.Values.Contains(x.Segment)).ToList();
        var allocated = allocatableFacts.Sum(x => x.Allocations.Sum(a => a.Amount));
        var allocatable = allocatableFacts.Sum(x => x.Amount);
        var estimated = allocatableFacts.Sum(x => x.Allocations
            .Where(a => a.Status == AllocationStatus.Estimated)
            .Sum(a => a.Amount));

        return new ProjectEconomySummary(
            ExpectedIncome: Sum(EconomicDirection.Income),
            KnownExpenditure: Sum(EconomicDirection.Expenditure),
            Received: Sum(EconomicDirection.Receipt),
            Paid: Sum(EconomicDirection.Payment),
            FundingGap: Sum(EconomicDirection.Payment) - Sum(EconomicDirection.Receipt),
            AllocatableAmount: allocatable,
            AllocatedAmount: allocated,
            UnallocatedAmount: Math.Max(0m, allocatable - allocated),
            EstimatedAllocationAmount: estimated,
            CoverageRate: allocatable == 0 ? 0m : Math.Min(allocated / allocatable, 1m),
            PendingConfirmationAmount: facts.Where(x => x.Status == ExternalFactStatus.PendingConfirmation).Sum(x => x.Amount),
            ExpiredAmount: facts.Where(x => x.Status == ExternalFactStatus.Expired).Sum(x => x.Amount),
            LatestAsOfDate: facts.Count == 0 ? null : facts.Max(x => x.AsOfDate),
            Segments: facts.GroupBy(x => x.Segment)
                .Select(g => new SegmentAmountSummary(g.Key, g.Sum(x => x.Amount), g.Count(), g.Max(x => x.AsOfDate)))
                .OrderBy(x => x.Segment)
                .ToList());
    }

    private async Task EnsureProjectAsync(int projectId, CancellationToken ct)
    {
        if (!await _db.Projects.AnyAsync(x => x.Id == projectId, ct))
            throw new InvalidOperationException($"未找到项目 {projectId}。");
    }
}
