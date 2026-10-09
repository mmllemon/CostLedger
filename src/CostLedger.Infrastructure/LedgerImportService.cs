using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>导入结果：成功条数 / 跳过条数 / 错误明细（按行）。</summary>
public record ImportResult(int Created, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// 台账 CSV 导入（Excel 可另存为 CSV）：按表头名映射，逐行校验，错误不中断整批。
/// 支持：变更/签证、材料收料、收款、付款。只做登记（铁律：不碰成本脊柱）。
/// </summary>
public class LedgerImportService
{
    private readonly AppDbContext _db;
    public LedgerImportService(AppDbContext db) => _db = db;

    public async Task<ImportResult> ImportAsync(int projectId, LedgerKind kind, string csvText)
    {
        var rows = CsvUtil.Parse(csvText);
        if (rows.Count == 0) return new ImportResult(0, 0, new[] { "文件为空" });

        var header = rows[0].Select(h => h.Trim().Trim('\uFEFF')).ToArray();
        var contracts = await _db.Contracts.ToDictionaryAsync(c => c.Code, c => c.Id);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Name, p => p.Id);

        int created = 0, skipped = 0;
        var errors = new List<string>();

        for (int r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            try
            {
                bool ok = kind switch
                {
                    LedgerKind.ChangeOrder => AddChangeOrder(projectId, header, cells, contracts, errors, r + 1),
                    LedgerKind.Material => AddMaterial(projectId, header, cells, errors, r + 1),
                    LedgerKind.Receipt => AddPayment(projectId, PaymentDirection.Receive, header, cells, contracts, parties, errors, r + 1),
                    LedgerKind.Payment => AddPayment(projectId, PaymentDirection.Pay, header, cells, contracts, parties, errors, r + 1),
                    _ => throw new InvalidOperationException($"该台账不支持导入：{kind}")
                };
                if (ok) created++; else skipped++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"第 {r + 1} 行：{ex.Message}");
            }
        }

        await _db.SaveChangesAsync();
        return new ImportResult(created, skipped, errors);
    }

    // ---------- 各台账映射 ----------
    private bool AddChangeOrder(int projectId, string[] header, string[] cells, Dictionary<string, int> contracts, List<string> errors, int line)
    {
        var code = Get(header, cells, "编号", "Code");
        if (string.IsNullOrEmpty(code)) { errors.Add($"第 {line} 行：编号为空"); return false; }

        _db.ChangeOrders.Add(new ChangeOrder
        {
            Code = code,
            ProjectId = projectId,
            Type = ParseChangeType(Get(header, cells, "类型")),
            Title = Get(header, cells, "内容", "Title"),
            Amount = ParseMoney(Get(header, cells, "金额(±)", "金额", "Amount")),
            OrderDate = ParseDate(Get(header, cells, "日期"), line),
            ContractId = ResolveContract(Get(header, cells, "关联合同", "合同"), contracts),
            Status = Get(header, cells, "状态").Contains("确认") ? DocStatus.Confirmed : DocStatus.Draft
        });
        return true;
    }

    private bool AddMaterial(int projectId, string[] header, string[] cells, List<string> errors, int line)
    {
        var code = Get(header, cells, "编号", "Code");
        if (string.IsNullOrEmpty(code)) { errors.Add($"第 {line} 行：编号为空"); return false; }
        var qty = ParseMoney(Get(header, cells, "数量"));
        var price = ParseMoney(Get(header, cells, "单价"));
        var amount = ParseMoney(Get(header, cells, "金额"));
        if (amount == 0 && qty != 0 && price != 0) amount = qty * price;

        _db.MaterialReceipts.Add(new MaterialReceipt
        {
            Code = code,
            ProjectId = projectId,
            MaterialName = Get(header, cells, "材料", "名称"),
            Spec = Get(header, cells, "规格"),
            Unit = Get(header, cells, "单位"),
            Source = Get(header, cells, "来源").Contains("甲供") ? MaterialSource.Owner : MaterialSource.Self,
            Quantity = qty,
            UnitPrice = price,
            Amount = amount,
            ReceiptDate = ParseDate(Get(header, cells, "收料日期", "日期"), line)
        });
        return true;
    }

    private bool AddPayment(int projectId, PaymentDirection dir, string[] header, string[] cells,
        Dictionary<string, int> contracts, Dictionary<string, int> parties, List<string> errors, int line)
    {
        var code = Get(header, cells, "编号", "Code");
        if (string.IsNullOrEmpty(code)) { errors.Add($"第 {line} 行：编号为空"); return false; }

        _db.Payments.Add(new Payment
        {
            Code = code,
            Direction = dir,
            ContractId = ResolveContract(Get(header, cells, "合同"), contracts),
            PartyId = ResolveParty(Get(header, cells, "往来单位", "单位"), parties),
            Type = ParsePaymentType(Get(header, cells, "类型")),
            Amount = ParseMoney(Get(header, cells, "金额", "Amount")),
            Date = ParseDate(Get(header, cells, "日期"), line),
            Remark = Get(header, cells, "备注")
        });
        return true;
    }

    // ---------- 工具 ----------
    private static string Get(string[] header, string[] cells, params string[] names)
    {
        foreach (var n in names)
        {
            int idx = Array.IndexOf(header, n);
            if (idx >= 0 && idx < cells.Length) return cells[idx].Trim();
        }
        return "";
    }

    private static int? ResolveContract(string code, Dictionary<string, int> contracts)
        => !string.IsNullOrEmpty(code) && contracts.TryGetValue(code, out var id) ? id : null;
    private static int ResolveParty(string name, Dictionary<string, int> parties)
        => !string.IsNullOrEmpty(name) && parties.TryGetValue(name, out var id) ? id : 0;

    private static decimal ParseMoney(string s)
        => decimal.TryParse(s.Replace(",", "").Replace("+", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static DateOnly ParseDate(string s, int line)
        => DateOnly.TryParse(s, out var d) ? d : throw new FormatException($"日期无法解析：{s}");

    private static ChangeOrderType ParseChangeType(string s) => s switch
    {
        var x when x.Contains("签证") => ChangeOrderType.SiteVariation,
        var x when x.Contains("索赔") => ChangeOrderType.Claim,
        _ => ChangeOrderType.DesignChange
    };

    private static PaymentType ParsePaymentType(string s) => s switch
    {
        var x when x.Contains("预付") => PaymentType.Advance,
        var x when x.Contains("结算") => PaymentType.Settlement,
        var x when x.Contains("质保") => PaymentType.Retention,
        _ => PaymentType.Progress
    };
}
