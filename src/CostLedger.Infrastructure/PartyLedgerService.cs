using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>按项目分组：合同额 / 累计付款 / 未付 / 累计收款</summary>
public record PartyProjectRow(string ProjectName, decimal ContractAmount, decimal Paid, decimal Unpaid, decimal Received);

/// <summary>合同明细：项目 / 合同 / 类型 / 金额 / 已付 / 未付</summary>
public record PartyContractRow(string ProjectName, string ContractCode, string TypeCode, decimal Amount, decimal Paid, decimal Unpaid);

/// <summary>
/// 某往来单位的跨项目汇总（供应商/建设单位视角）。
/// </summary>
public record PartyLedger(
    string PartyName, string? RoleCode,
    decimal ContractTotal, decimal UpstreamAmount, decimal DownstreamAmount,
    decimal Paid, decimal Unpaid, decimal Received, decimal Unreceived,
    int ProjectCount, int ContractCount,
    IReadOnlyList<PartyProjectRow> ByProject,
    IReadOnlyList<PartyContractRow> Contracts,
    /// <summary>数据截至日期：本台账所依据的最晚合同签约日/收付款日。口径声明必须随报表一起打印。</summary>
    DateOnly? AsOfDate = null);

/// <summary>
/// 往来单位台账（跨项目）：选一个单位 → 一下子看清它在公司所有项目的合同额、已付/未付、已收/未收。
/// 只读聚合（铁律）。往来单位是全公司共享库，故本视图天然跨项目。
/// </summary>
public class PartyLedgerService
{
    private readonly AppDbContext _db;
    public PartyLedgerService(AppDbContext db) => _db = db;

    public Task<List<Party>> ListPartiesAsync()
        => _db.Parties.OrderBy(p => p.Name).ToListAsync();

    public async Task<PartyLedger> BuildAsync(int partyId)
    {
        var party = await _db.Parties.FindAsync(partyId);

        var contracts = await _db.Contracts
            .Include(c => c.Project)
            .Where(c => c.PartyId == partyId)
            .ToListAsync();

        var partiesPayments = await _db.Payments
            .Where(p => p.PartyId == partyId)
            .ToListAsync();

        // 合同 → 项目名
        var projectName = new Dictionary<int, string>();
        foreach (var c in contracts)
            if (c.Project is not null) projectName[c.Id] = c.Project.Name;

        var contractAmountById = contracts.ToDictionary(c => c.Id, c => c.Amount);
        var contractProjectById = contracts.ToDictionary(c => c.Id, c => c.Project?.Name ?? "（未知项目）");

        decimal SumPaid(System.Collections.Generic.IEnumerable<Payment> ps) => ps.Where(p => p.Direction == PaymentDirection.Pay).Sum(p => p.Amount);
        decimal SumReceived(System.Collections.Generic.IEnumerable<Payment> ps) => ps.Where(p => p.Direction == PaymentDirection.Receive).Sum(p => p.Amount);

        var contractTotal = contracts.Sum(c => c.Amount);
        var upstream = contracts.Where(c => c.TypeCode == "Main").Sum(c => c.Amount);
        var downstream = contractTotal - upstream;
        var paid = SumPaid(partiesPayments);
        var received = SumReceived(partiesPayments);

        // 按项目分组
        var byProject = contracts
            .GroupBy(c => contractProjectById[c.Id])
            .Select(g =>
            {
                var ids = g.Select(c => c.Id).ToHashSet();
                var amt = g.Sum(c => c.Amount);
                var p = SumPaid(partiesPayments.Where(x => x.ContractId is not null && ids.Contains(x.ContractId.Value)));
                var r = SumReceived(partiesPayments.Where(x => x.ContractId is not null && ids.Contains(x.ContractId.Value)));
                return new PartyProjectRow(g.Key, amt, p, amt - p, r);
            })
            .OrderByDescending(x => x.ContractAmount)
            .ToList();

        // 合同明细（逐合同：已付/未付）
        var contractRows = contracts
            .Select(c =>
            {
                var p = SumPaid(partiesPayments.Where(x => x.ContractId == c.Id));
                return new PartyContractRow(contractProjectById[c.Id], c.Code, c.TypeCode, c.Amount, p, c.Amount - p);
            })
            .OrderBy(x => x.ProjectName).ThenBy(x => x.ContractCode)
            .ToList();

        // 数据截至：取本单位所依据的最晚业务日期（合同签约日 / 收付款日）。
        var asOfDates = new List<DateOnly>();
        asOfDates.AddRange(contracts.Where(c => c.SignDate != default).Select(c => c.SignDate));
        asOfDates.AddRange(partiesPayments.Where(p => p.Date != default).Select(p => p.Date));
        DateOnly? asOf = asOfDates.Count == 0 ? null : asOfDates.Max();

        return new PartyLedger(
            party?.Name ?? $"单位 {partyId}", party?.RoleCode,
            contractTotal, upstream, downstream,
            paid, downstream - paid, received, upstream - received,
            byProject.Count, contracts.Count, byProject, contractRows, asOf);
    }
}
