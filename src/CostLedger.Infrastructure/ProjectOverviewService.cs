using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>项目总览：一屏给出项目的钱与成本全貌。</summary>
public record ProjectOverview(
    string ProjectName,
    decimal UpstreamContract, decimal DownstreamContract,
    decimal ReceivedCumulative, decimal Receivable,
    decimal PaidCumulative, decimal Payable,
    decimal ChangePositive, decimal ChangeNegative, decimal ChangeNet,
    decimal Target, decimal Committed, decimal Incurred, decimal WorkDoneNotSettled, decimal Remaining, decimal EstimatedFinal,
    decimal GrossProfit);

/// <summary>
/// 项目总览服务：把 合同 / 收付款 / 变更 / 成本脊柱 汇总为一屏数字。
/// 只读、不写回（铁律）。定位：项目成本与资金台账的"首页"。
/// </summary>
public class ProjectOverviewService
{
    private readonly AppDbContext _db;
    private readonly ICostSpineService _spine;

    public ProjectOverviewService(AppDbContext db, ICostSpineService spine)
    {
        _db = db;
        _spine = spine;
    }

    public async Task<ProjectOverview> BuildAsync(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        var name = project?.Name ?? $"项目 {projectId}";

        var contracts = await _db.Contracts.Where(c => c.ProjectId == projectId).ToListAsync();
        var upstream = contracts.Where(c => c.TypeCode == "Main").Sum(c => c.Amount);
        var downstream = contracts.Where(c => c.TypeCode != "Main").Sum(c => c.Amount);

        var payments = await _db.Payments
            .Where(p => p.Contract != null && p.Contract.ProjectId == projectId)
            .ToListAsync();
        var received = payments.Where(p => p.Direction == PaymentDirection.Receive).Sum(p => p.Amount);
        var paid = payments.Where(p => p.Direction == PaymentDirection.Pay).Sum(p => p.Amount);

        var changes = await _db.ChangeOrders
            .Where(c => c.ProjectId == projectId && c.Status == DocStatus.Confirmed)
            .ToListAsync();
        var changePos = changes.Where(c => c.Amount > 0).Sum(c => c.Amount);
        var changeNeg = changes.Where(c => c.Amount < 0).Sum(c => c.Amount);

        var spine = await _spine.GetSpineAsync(projectId);
        var target = spine.Sum(l => l.Target);
        var committed = spine.Sum(l => l.Committed);
        var incurred = spine.Sum(l => l.Incurred);
        var wdns = spine.Sum(l => l.WorkDoneNotSettled);
        var remaining = spine.Sum(l => l.Remaining);
        var eac = spine.Sum(l => l.EstimatedFinal);

        return new ProjectOverview(
            name,
            upstream, downstream,
            received, upstream - received,          // 未收 = 对上合同额 − 累计收款（合同额为基数）
            paid, downstream - paid,                // 未付 = 对下合同额 − 累计付款
            changePos, changeNeg, changePos + changeNeg,
            target, committed, incurred, wdns, remaining, eac,
            upstream - eac);                        // 毛利 = 对上合同额 − 预计最终
    }
}
