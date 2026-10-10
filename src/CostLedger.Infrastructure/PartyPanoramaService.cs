using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>单位在某项目上的敞口行。角色来自「项目+板块」关系，不是单位档案上的全局标签。</summary>
public sealed record PartyPanoramaProjectRow(
    string ProjectName,
    string Roles,
    decimal ContractAmount,
    decimal Paid,
    decimal Unpaid,
    decimal Received,
    decimal Unreceived,
    DateOnly? AsOfDate);

/// <summary>合同明细行。</summary>
public sealed record PartyPanoramaContractRow(
    string ProjectName, string ContractCode, string TypeCode,
    decimal Amount, decimal Paid, decimal Unpaid);

/// <summary>
/// 往来单位全景（新口径，跨全部项目）：
/// 合同额来自合同；累计付款/收款来自外部金额事实（已付/已收板块），而非旧 Payment 表；
/// 未付/未收是"合同额 − 已登记金额"的推算差额，只作内部管理信息，不等于财务应付/应收。
/// </summary>
public sealed record PartyPanorama(
    string PartyName,
    decimal ContractTotal,
    decimal UpstreamAmount,
    decimal DownstreamAmount,
    decimal Paid,
    decimal Unpaid,
    decimal Received,
    decimal Unreceived,
    int ProjectCount,
    int ContractCount,
    DateOnly? AsOfDate,
    IReadOnlyList<PartyPanoramaProjectRow> ByProject,
    IReadOnlyList<PartyPanoramaContractRow> Contracts);

/// <summary>往来单位全景取数（只读聚合）。</summary>
public sealed class PartyPanoramaService
{
    private readonly AppDbContext _db;
    public PartyPanoramaService(AppDbContext db) => _db = db;

    public Task<List<Party>> ListPartiesAsync()
        => _db.Parties.OrderBy(p => p.Name).ToListAsync();

    public async Task<PartyPanorama> BuildAsync(int partyId)
    {
        var party = await _db.Parties.FindAsync(partyId);

        var contracts = await _db.Contracts
            .AsNoTracking()
            .Include(c => c.Project)
            .Where(c => c.PartyId == partyId)
            .ToListAsync();

        // 外部金额事实：按单位跨全部项目取已付/已收（资金状态板块）。
        var facts = await _db.ExternalAmountFacts
            .AsNoTracking()
            .Include(x => x.Project)
            .Where(x => x.PartyId == partyId)
            .ToListAsync();

        // 角色来自「项目+板块」关系。
        var relationships = await _db.ProjectPartyRelationships
            .AsNoTracking()
            .Include(r => r.Project)
            .Where(r => r.PartyId == partyId)
            .ToListAsync();

        decimal PaidIn(System.Collections.Generic.IEnumerable<ExternalAmountFact> fs)
            => fs.Where(x => x.Direction == EconomicDirection.Payment).Sum(x => x.Amount);
        decimal ReceivedIn(System.Collections.Generic.IEnumerable<ExternalAmountFact> fs)
            => fs.Where(x => x.Direction == EconomicDirection.Receipt).Sum(x => x.Amount);

        var contractTotal = contracts.Sum(c => c.Amount);
        var upstream = contracts.Where(c => c.TypeCode == "Main").Sum(c => c.Amount);
        var downstream = contractTotal - upstream;
        var paid = PaidIn(facts);
        var received = ReceivedIn(facts);

        var projectName = contracts.ToDictionary(c => c.Id, c => c.Project?.Name ?? "（未知项目）");

        // 按项目：合同额（该项目合同合计）+ 该项目外部事实的已付/已收 + 该项目关系里的角色。
        var projectIds = contracts.Select(c => c.ProjectId)
            .Concat(facts.Select(f => f.ProjectId))
            .Concat(relationships.Select(r => r.ProjectId))
            .Distinct();

        var byProject = new List<PartyPanoramaProjectRow>();
        foreach (var pid in projectIds)
        {
            var name = contracts.FirstOrDefault(c => c.ProjectId == pid)?.Project?.Name
                ?? facts.FirstOrDefault(f => f.ProjectId == pid)?.Project?.Name
                ?? relationships.FirstOrDefault(r => r.ProjectId == pid)?.Project?.Name
                ?? $"项目 {pid}";
            var amt = contracts.Where(c => c.ProjectId == pid).Sum(c => c.Amount);
            var pfs = facts.Where(f => f.ProjectId == pid).ToList();
            var p = PaidIn(pfs);
            var r = ReceivedIn(pfs);
            var roles = string.Join(" · ", relationships.Where(x => x.ProjectId == pid)
                .Select(x => BusinessSegmentRules.DisplayName(x.Segment)).Distinct());
            var asOf = pfs.Select(x => x.AsOfDate).DefaultIfEmpty().Max();
            byProject.Add(new PartyPanoramaProjectRow(name, string.IsNullOrEmpty(roles) ? "—" : roles, amt, p, Math.Max(0m, amt - p), r, 0m, asOf == default ? null : asOf));
        }
        byProject = byProject.OrderByDescending(x => x.ContractAmount).ThenBy(x => x.ProjectName).ToList();

        var contractRows = contracts
            .Select(c =>
            {
                var p = facts.Where(f => f.ContractId == c.Id).Where(f => f.Direction == EconomicDirection.Payment).Sum(f => f.Amount);
                return new PartyPanoramaContractRow(projectName[c.Id], c.Code, ContractTypeName(c.TypeCode), c.Amount, p, Math.Max(0m, c.Amount - p));
            })
            .OrderBy(x => x.ProjectName).ThenBy(x => x.ContractCode)
            .ToList();

        var asOfDates = new List<DateOnly>();
        asOfDates.AddRange(contracts.Where(c => c.SignDate != default).Select(c => c.SignDate));
        asOfDates.AddRange(facts.Select(f => f.AsOfDate).Where(d => d != default));
        DateOnly? asOfAll = asOfDates.Count == 0 ? null : asOfDates.Max();

        return new PartyPanorama(
            party?.Name ?? $"单位 {partyId}",
            contractTotal, upstream, downstream,
            paid, Math.Max(0m, downstream - paid),
            received, Math.Max(0m, upstream - received),
            byProject.Count, contracts.Count, asOfAll, byProject, contractRows);
    }

    private static string ContractTypeName(string? typeCode) => typeCode switch
    {
        "Main" => "对上（主合同）",
        null or "" => "未分类",
        _ => typeCode!
    };
}
