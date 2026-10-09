using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>台账种类</summary>
public enum LedgerKind { Contract, Receipt, Payment, ChangeOrder, Material, Invoice, Settlement, Boq }

/// <summary>一张台账：标题 + 明细行 + 合计行（可多行）。供界面显示与"正式版式"打印共用。</summary>
public record LedgerResult(string Title, IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, IReadOnlyList<string[]> Footers);

/// <summary>
/// 台账引擎：把 7 类业务对象统一为"明细 + 合计"的台账视图（登记+累计）。
/// 只读（铁律）。界面「台账中心」与打印（正式版式）共用同一份结果。
/// </summary>
public class LedgerService
{
    private readonly AppDbContext _db;
    public LedgerService(AppDbContext db) => _db = db;

    public async Task<LedgerResult> BuildAsync(int projectId, LedgerKind kind) => kind switch
    {
        LedgerKind.Contract => await ContractAsync(projectId),
        LedgerKind.Receipt => await PaymentLedgerAsync(projectId, PaymentDirection.Receive, "收款台账"),
        LedgerKind.Payment => await PaymentLedgerAsync(projectId, PaymentDirection.Pay, "付款台账"),
        LedgerKind.ChangeOrder => await ChangeOrderAsync(projectId),
        LedgerKind.Material => await MaterialAsync(projectId),
        LedgerKind.Invoice => await InvoiceAsync(projectId),
        LedgerKind.Settlement => await SettlementAsync(projectId),
        LedgerKind.Boq => await BoqLedgerAsync(projectId),
        _ => new LedgerResult("台账", Array.Empty<string>(), Array.Empty<string[]>(), Array.Empty<string[]>())
    };

    // ---------- 合同台账 ----------
    private async Task<LedgerResult> ContractAsync(int projectId)
    {
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        var rows = await _db.Contracts.Where(c => c.ProjectId == projectId).OrderBy(c => c.Code).ToListAsync();
        var data = rows.Select(c => new[]
        {
            c.Code, c.TypeCode, parties.GetValueOrDefault(c.PartyId, ""), c.Title ?? "",
            Money(c.Amount), c.TaxRate.ToString("P0", CultureInfo.CurrentCulture), Date(c.SignDate), c.Status ?? ""
        }).ToList();
        return new LedgerResult("合同台账",
            new[] { "合同编号", "类型", "对方单位", "合同名称", "金额", "税率", "签约日期", "状态" },
            data,
            new[] { Blank(8, 4, Money(rows.Sum(x => x.Amount))) });
    }

    // ---------- 收款 / 付款台账 ----------
    private async Task<LedgerResult> PaymentLedgerAsync(int projectId, PaymentDirection dir, string title)
    {
        var contracts = await _db.Contracts.ToDictionaryAsync(c => c.Id, c => c.Code);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        var rows = await _db.Payments
            .Where(p => p.Contract != null && p.Contract.ProjectId == projectId && p.Direction == dir)
            .OrderBy(p => p.Date).ToListAsync();
        var data = rows.Select(p => new[]
        {
            Date(p.Date), p.Code, p.ContractId is null ? "" : contracts.GetValueOrDefault(p.ContractId.Value, ""),
            parties.GetValueOrDefault(p.PartyId, ""), PaymentTypeName(p.Type), Money(p.Amount),
            p.ContainsMigrantWage ? "含农民工" : ""
        }).ToList();
        return new LedgerResult(title,
            new[] { "日期", "编号", "合同", "往来单位", "类型", "金额", "备注" },
            data,
            new[] { Blank(7, 5, Money(rows.Sum(x => x.Amount))) });
    }

    // ---------- 变更 / 签证台账 ----------
    private async Task<LedgerResult> ChangeOrderAsync(int projectId)
    {
        var contracts = await _db.Contracts.ToDictionaryAsync(c => c.Id, c => c.Code);
        var rows = await _db.ChangeOrders.Where(c => c.ProjectId == projectId).OrderBy(c => c.Code).ToListAsync();
        var data = rows.Select(c => new[]
        {
            c.Code, ChangeTypeName(c.Type), c.Title,
            (c.Amount >= 0 ? "+" : "") + Money(c.Amount), Date(c.OrderDate),
            c.ContractId is null ? "" : contracts.GetValueOrDefault(c.ContractId.Value, ""),
            DocStatusName(c.Status)
        }).ToList();
        var pos = rows.Where(c => c.Amount > 0).Sum(c => c.Amount);
        var neg = rows.Where(c => c.Amount < 0).Sum(c => c.Amount);
        return new LedgerResult("变更 / 签证台账",
            new[] { "编号", "类型", "内容", "金额(±)", "日期", "关联合同", "状态" },
            data,
            new List<string[]>
            {
                Blank(7, 2, "正变更合计", Money(pos)),
                Blank(7, 2, "负变更合计", Money(neg)),
                Blank(7, 2, "净额", Money(pos + neg))
            });
    }

    // ---------- 材料收料台账 ----------
    private async Task<LedgerResult> MaterialAsync(int projectId)
    {
        var rows = await _db.MaterialReceipts.Where(m => m.ProjectId == projectId).OrderBy(m => m.Code).ToListAsync();
        var data = rows.Select(m => new[]
        {
            m.Code, m.MaterialName, m.Spec ?? "", m.Unit ?? "",
            m.Source == MaterialSource.Owner ? "甲供" : "乙供",
            m.Quantity.ToString("0.####", CultureInfo.CurrentCulture),
            Money(m.UnitPrice), Money(m.Amount), Date(m.ReceiptDate)
        }).ToList();
        return new LedgerResult("材料收料台账",
            new[] { "编号", "材料", "规格", "单位", "来源", "数量", "单价", "金额", "收料日期" },
            data,
            new[] { Blank(9, 7, Money(rows.Sum(x => x.Amount))) });
    }

    // ---------- 发票台账 ----------
    private async Task<LedgerResult> InvoiceAsync(int projectId)
    {
        var rows = await _db.Invoices.Where(i => i.ProjectId == projectId).OrderBy(i => i.InvoiceDate).ToListAsync();
        var data = rows.Select(i => new[]
        {
            i.Code, i.Direction == InvoiceDirection.Input ? "进项" : "销项",
            i.Category == InvoiceCategory.Special ? "专用" : "普通",
            Money(i.NetAmount), Money(i.TaxAmount), Money(i.TotalAmount), Date(i.InvoiceDate), i.Status ?? ""
        }).ToList();
        return new LedgerResult("发票台账",
            new[] { "发票号", "方向", "种类", "不含税", "税额", "价税合计", "日期", "状态" },
            data,
            new[] { Blank(8, 5, Money(rows.Sum(x => x.TotalAmount))) });
    }

    // ---------- 工程量清单台账（目标成本） ----------
    private async Task<LedgerResult> BoqLedgerAsync(int projectId)
    {
        var cats = await _db.CostCategories.ToDictionaryAsync(c => c.Id, c => c.Name);
        var wbs = await _db.WbsItems.ToDictionaryAsync(w => w.Id, w => w.FullPath);
        var rows = await _db.BoqLines.Where(b => b.ProjectId == projectId).OrderBy(b => b.Code).ToListAsync();
        var data = rows.Select(b => new[]
        {
            b.Code, b.ItemName, b.Feature ?? "", b.Unit ?? "",
            b.Quantity.ToString("0.####", CultureInfo.CurrentCulture),
            Money(b.UnitPrice), Money(b.Amount),
            b.CostCategoryId is null ? "" : cats.GetValueOrDefault(b.CostCategoryId.Value, ""),
            b.WbsItemId is null ? "" : wbs.GetValueOrDefault(b.WbsItemId.Value, "")
        }).ToList();
        return new LedgerResult("工程量清单（目标成本）",
            new[] { "项目编码", "项目名称", "项目特征", "单位", "工程量", "综合单价", "合价", "成本科目", "部位" },
            data,
            new[] { Blank(9, 6, Money(rows.Sum(x => x.Amount))) });
    }

    // ---------- 竣工结算台账 ----------
    private async Task<LedgerResult> SettlementAsync(int projectId)
    {
        var contracts = await _db.Contracts.ToDictionaryAsync(c => c.Id, c => c.Code);
        var rows = await _db.Settlements.Where(s => s.ProjectId == projectId).OrderBy(s => s.Code).ToListAsync();
        var data = rows.Select(s => new[]
        {
            s.Code, s.ContractId is null ? "" : contracts.GetValueOrDefault(s.ContractId.Value, ""),
            Money(s.OriginalAmount), Money(s.AdjustAmount), Money(s.SettledAmount), Date(s.SettleDate), DocStatusName(s.Status)
        }).ToList();
        return new LedgerResult("竣工结算台账",
            new[] { "编号", "合同", "原合同额", "调整额", "结算金额", "日期", "状态" },
            data,
            new[] { Blank(7, 4, Money(rows.Sum(x => x.SettledAmount))) });
    }

    // ---------- 工具 ----------
    private static string Money(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string[] Blank(int width, int col, params string[] cells)
    {
        var arr = new string[width];
        for (int i = 0; i < width; i++) arr[i] = "";
        for (int i = 0; i < cells.Length && col + i < width; i++) arr[col + i] = cells[i];
        return arr;
    }

    private static string PaymentTypeName(PaymentType t) => t switch
    {
        PaymentType.Advance => "预付款", PaymentType.Progress => "进度款",
        PaymentType.Settlement => "结算款", PaymentType.Retention => "质保返还", _ => t.ToString()
    };
    private static string ChangeTypeName(ChangeOrderType t) => t switch
    {
        ChangeOrderType.DesignChange => "设计变更", ChangeOrderType.SiteVariation => "现场签证",
        ChangeOrderType.Claim => "索赔", _ => t.ToString()
    };
    private static string DocStatusName(DocStatus s) => s == DocStatus.Confirmed ? "已确认" : "草稿";
}
