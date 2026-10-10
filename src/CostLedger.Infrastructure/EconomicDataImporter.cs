using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>导入结果条目：一条被接受或拒绝的记录及其原因。</summary>
public sealed record ImportEntry(string Table, string Key, bool Imported, string Message);

/// <summary>
/// 导入报告：逐条结果 + 每个项目的经济摘要。
/// 拒绝不是失败，而是领域规则对真实数据的裁决——报告要能直接回答"哪一行不符合口径、为什么"。
/// </summary>
public sealed record ImportReport(IReadOnlyList<ImportEntry> Entries, IReadOnlyList<string> ProjectSummaries)
{
    public int Imported => Entries.Count(e => e.Imported);
    public int Rejected => Entries.Count(e => !e.Imported);
    public IEnumerable<ImportEntry> Rejections => Entries.Where(e => !e.Imported);
}

/// <summary>
/// 从表格目录（制表符分隔的文本，每表一个文件）导入项目经济数据。
/// 全部经 EconomicFactsService / CostForecastService，不绕过领域规则：
/// 跨项目分配、板块与方向不符、超出来源金额等都会被真实服务拒绝并记录在报告里。
/// </summary>
public sealed class EconomicDataImporter
{
    private readonly AppDbContext _db;
    private readonly EconomicFactsService _facts;
    private readonly CostForecastService _forecast;

    public EconomicDataImporter(AppDbContext db)
    {
        _db = db;
        _facts = new EconomicFactsService(db);
        _forecast = new CostForecastService(db);
    }

    public async Task<ImportReport> ImportFromDirectoryAsync(string dir, CancellationToken ct = default)
    {
        var entries = new List<ImportEntry>();
        string File(string name) => Path.Combine(dir, name + ".tsv");

        // 1-项目
        foreach (var r in Read(File("1-项目")))
        {
            var key = Col(r, "项目编号");
            try
            {
                if (string.IsNullOrWhiteSpace(key)) { entries.Add(new("1-项目", "(空)", false, "缺少项目编号")); continue; }
                if (await _db.Projects.AnyAsync(x => x.Code == key, ct)) { entries.Add(new("1-项目", key, true, "已存在，跳过")); continue; }
                _db.Projects.Add(new Project
                {
                    Code = key, Name = Col(r, "项目名称"),
                    OwnerName = NullIfEmpty(Col(r, "建设单位")),
                    StartDate = Date(Col(r, "计划开始")),
                    PlannedEndDate = string.IsNullOrWhiteSpace(Col(r, "计划完工")) ? null : Date(Col(r, "计划完工"))
                });
                await _db.SaveChangesAsync(ct);
                entries.Add(new("1-项目", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("1-项目", key, false, ex.Message)); }
        }

        // 2-往来单位
        foreach (var r in Read(File("2-往来单位")))
        {
            var key = Col(r, "单位名称");
            try
            {
                if (string.IsNullOrWhiteSpace(key)) { entries.Add(new("2-往来单位", "(空)", false, "缺少单位名称")); continue; }
                if (await _db.Parties.AnyAsync(x => x.Name == key, ct)) { entries.Add(new("2-往来单位", key, true, "已存在，跳过")); continue; }
                _db.Parties.Add(new Party { Name = key, TaxNo = NullIfEmpty(Col(r, "统一社会信用代码")), Contact = NullIfEmpty(Col(r, "联系人")), RoleCode = "Supplier" });
                await _db.SaveChangesAsync(ct);
                entries.Add(new("2-往来单位", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("2-往来单位", key, false, ex.Message)); }
        }

        // 3-成本控制项
        foreach (var r in Read(File("3-成本控制项")))
        {
            var projCode = Col(r, "项目编号");
            var name = Col(r, "控制项名称");
            var key = $"{projCode}/{Col(r, "控制项编号")} {name}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("3-成本控制项", key, false, $"未找到项目 {projCode}")); continue; }
                int.TryParse(Col(r, "排序"), out var sort);
                _db.CostControlItems.Add(new CostControlItem
                {
                    ProjectId = project.Id, Code = Col(r, "控制项编号"), Name = name, SortOrder = sort,
                    IsActive = Col(r, "启用") != "否"
                });
                await _db.SaveChangesAsync(ct);
                entries.Add(new("3-成本控制项", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("3-成本控制项", key, false, ex.Message)); }
        }

        // 4-业务关系
        foreach (var r in Read(File("4-业务关系")))
        {
            var projCode = Col(r, "项目编号");
            var partyName = Col(r, "单位名称");
            var segLabel = Col(r, "业务板块");
            var key = $"{projCode}/{partyName}/{segLabel}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("4-业务关系", key, false, $"未找到项目 {projCode}")); continue; }
                var party = await _db.Parties.SingleOrDefaultAsync(x => x.Name == partyName, ct);
                if (party is null) { entries.Add(new("4-业务关系", key, false, $"未找到单位 {partyName}")); continue; }
                var segment = EconomicLabels.ParseSegment(segLabel);
                if (segment is null) { entries.Add(new("4-业务关系", key, false, $"无法识别业务板块「{segLabel}」")); continue; }
                var direction = EconomicLabels.ParseDirection(Col(r, "金额方向"));
                if (direction is null) { entries.Add(new("4-业务关系", key, false, $"无法识别金额方向「{Col(r, "金额方向")}」")); continue; }
                var roleCode = EconomicLabels.ParseRoleCode(Col(r, "角色"));
                if (roleCode is null) { entries.Add(new("4-业务关系", key, false, "缺少角色")); continue; }

                int? contractId = null;
                var ccode = Col(r, "合同编号(可空)");
                if (!string.IsNullOrWhiteSpace(ccode))
                {
                    var c = await _db.Contracts.FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Code == ccode, ct);
                    if (c is null) { entries.Add(new("4-业务关系", key, false, $"本项目未找到合同 {ccode}")); continue; }
                    contractId = c.Id;
                }

                await _facts.RegisterRelationshipAsync(new ProjectPartyRelationship
                {
                    ProjectId = project.Id, PartyId = party.Id, ContractId = contractId,
                    RoleCode = roleCode, Segment = segment.Value, Direction = direction.Value,
                    Note = NullIfEmpty(Col(r, "备注"))
                }, ct);
                entries.Add(new("4-业务关系", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("4-业务关系", key, false, ex.Message)); }
        }

        // 5-外部金额事实
        foreach (var r in Read(File("5-外部金额事实")))
        {
            var projCode = Col(r, "项目编号");
            var src = Col(r, "来源");
            var key = $"{projCode}/{src}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("5-外部金额事实", key, false, $"未找到项目 {projCode}")); continue; }
                var segment = EconomicLabels.ParseSegment(Col(r, "业务板块"));
                if (segment is null) { entries.Add(new("5-外部金额事实", key, false, $"无法识别业务板块「{Col(r, "业务板块")}」")); continue; }
                var direction = EconomicLabels.ParseDirection(Col(r, "金额方向"));
                if (direction is null) { entries.Add(new("5-外部金额事实", key, false, $"无法识别金额方向「{Col(r, "金额方向")}」")); continue; }
                var status = EconomicLabels.ParseStatus(Col(r, "数据状态"));
                if (status is null) { entries.Add(new("5-外部金额事实", key, false, $"无法识别数据状态「{Col(r, "数据状态")}」")); continue; }
                var precision = EconomicLabels.ParsePrecision(Col(r, "管理精度"));
                if (precision is null) { entries.Add(new("5-外部金额事实", key, false, $"无法识别管理精度「{Col(r, "管理精度")}」")); continue; }

                int? partyId = null;
                var pname = Col(r, "对方单位(可空)");
                if (!string.IsNullOrWhiteSpace(pname))
                {
                    var party = await _db.Parties.SingleOrDefaultAsync(x => x.Name == pname, ct);
                    if (party is null) { entries.Add(new("5-外部金额事实", key, false, $"未找到单位 {pname}")); continue; }
                    partyId = party.Id;
                }
                int? contractId = null;
                var ccode = Col(r, "合同编号(可空)");
                if (!string.IsNullOrWhiteSpace(ccode))
                {
                    var c = await _db.Contracts.FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Code == ccode, ct);
                    if (c is not null) contractId = c.Id;
                }

                await _facts.RegisterFactAsync(new ExternalAmountFact
                {
                    ProjectId = project.Id, PartyId = partyId, ContractId = contractId,
                    Segment = segment.Value, Direction = direction.Value, Amount = Money(Col(r, "金额")),
                    AsOfDate = Date(Col(r, "数据截至")), Status = status.Value, Precision = precision.Value,
                    Source = src, ProvidedBy = NullIfEmpty(Col(r, "提供方")), Description = NullIfEmpty(Col(r, "说明"))
                }, ct);
                entries.Add(new("5-外部金额事实", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("5-外部金额事实", key, false, ex.Message)); }
        }

        // 6-分配
        foreach (var r in Read(File("6-分配")))
        {
            var projCode = Col(r, "项目编号");
            var itemName = Col(r, "控制项名称");
            var src = Col(r, "来源描述(与5表\"来源\"一致)");
            var key = $"{projCode}/{itemName} ← {src}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("6-分配", key, false, $"未找到项目 {projCode}")); continue; }
                var item = await _db.CostControlItems.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.Name == itemName, ct);
                if (item is null) { entries.Add(new("6-分配", key, false, $"项目 {projCode} 中未找到控制项 {itemName}")); continue; }
                var fact = await _db.ExternalAmountFacts.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.Source == src, ct);
                if (fact is null) { entries.Add(new("6-分配", key, false, $"项目 {projCode} 中未找到来源《{src}》")); continue; }
                var precision = EconomicLabels.ParsePrecision(Col(r, "分配精度"))
                    ?? await _db.ManagementAllocations.Where(x => x.ExternalAmountFactId == fact.Id).Select(x => x.Precision).FirstOrDefaultAsync(ct);
                var status = EconomicLabels.ParseAllocationStatus(Col(r, "分配状态")) ?? AllocationStatus.Allocated;

                await _facts.AllocateAsync(fact.Id, item.Id, Money(Col(r, "分配金额")), precision, status, NullIfEmpty(Col(r, "备注")), ct);
                entries.Add(new("6-分配", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("6-分配", key, false, ex.Message)); }
        }

        // 7-预测版本
        var versionIds = new Dictionary<string, int>();
        foreach (var r in Read(File("7-预测版本")))
        {
            var projCode = Col(r, "项目编号");
            var dateStr = Col(r, "预测日期");
            var key = $"{projCode}/{dateStr}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("7-预测版本", key, false, $"未找到项目 {projCode}")); continue; }
                var conf = EconomicLabels.ParseConfidence(Col(r, "可信度")) ?? ForecastConfidence.Low;
                var v = await _forecast.CreateVersionAsync(project.Id, Date(dateStr), conf, NullIfEmpty(Col(r, "编制假设")), ct: ct);
                versionIds[$"{projCode}|{dateStr}"] = v.Id;
                entries.Add(new("7-预测版本", $"{projCode}/v{v.VersionNumber}", true, "已导入(草稿)"));
            }
            catch (Exception ex) { entries.Add(new("7-预测版本", key, false, ex.Message)); }
        }

        // 8-预测行
        foreach (var r in Read(File("8-预测行")))
        {
            var projCode = Col(r, "项目编号");
            var dateStr = Col(r, "预测日期");
            var itemName = Col(r, "控制项名称");
            var key = $"{projCode}/{itemName}";
            try
            {
                if (!versionIds.TryGetValue($"{projCode}|{dateStr}", out var vid))
                { entries.Add(new("8-预测行", key, false, $"项目 {projCode} 在 {dateStr} 没有对应的预测版本")); continue; }
                var project = await _db.Projects.SingleAsync(x => x.Code == projCode, ct);
                var item = await _db.CostControlItems.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.Name == itemName, ct);
                if (item is null) { entries.Add(new("8-预测行", key, false, $"项目 {projCode} 中未找到控制项 {itemName}")); continue; }
                await _forecast.UpsertLineAsync(vid, item.Id,
                    Money(Col(r, "目标成本")), Money(Col(r, "剩余估计")), Money(Col(r, "风险调整")), ct);
                entries.Add(new("8-预测行", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("8-预测行", key, false, ex.Message)); }
        }

        // 9-变化事项
        foreach (var r in Read(File("9-变化事项")))
        {
            var projCode = Col(r, "项目编号");
            var code = Col(r, "事项编号");
            var key = $"{projCode}/{code}";
            try
            {
                var project = await _db.Projects.SingleOrDefaultAsync(x => x.Code == projCode, ct);
                if (project is null) { entries.Add(new("9-变化事项", key, false, $"未找到项目 {projCode}")); continue; }
                var segment = EconomicLabels.ParseSegment(Col(r, "业务板块"));
                if (segment is null) { entries.Add(new("9-变化事项", key, false, $"无法识别业务板块「{Col(r, "业务板块")}」")); continue; }
                var status = EconomicLabels.ParseChangeStatus(Col(r, "状态"));
                if (status is null) { entries.Add(new("9-变化事项", key, false, $"无法识别状态「{Col(r, "状态")}」")); continue; }

                int? itemId = null;
                var itemName = Col(r, "控制项名称(可空)");
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    var it = await _db.CostControlItems.FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Name == itemName, ct);
                    itemId = it?.Id;
                    if (it is null) { entries.Add(new("9-变化事项", key, false, $"项目 {projCode} 中未找到控制项 {itemName}")); continue; }
                }

                await _forecast.RegisterChangeItemAsync(new ChangeItem
                {
                    ProjectId = project.Id, Code = code, Title = Col(r, "事项名称"),
                    Segment = segment.Value, CostControlItemId = itemId,
                    CostOfficerEstimate = MoneyOrNull(Col(r, "我方测算")),
                    CounterpartySubmitted = MoneyOrNull(Col(r, "对方报送")),
                    ExternallyConfirmed = MoneyOrNull(Col(r, "外部确认")),
                    IncludedInForecast = MoneyOrNull(Col(r, "纳入预测")),
                    Status = status.Value, AsOfDate = Date(Col(r, "数据截至")),
                    Source = Col(r, "来源"), ProvidedBy = NullIfEmpty(Col(r, "提供方"))
                }, ct);
                entries.Add(new("9-变化事项", key, true, "已导入"));
            }
            catch (Exception ex) { entries.Add(new("9-变化事项", key, false, ex.Message)); }
        }

        // 冻结标记为已冻结的版本
        foreach (var r in Read(File("7-预测版本")))
        {
            var projCode = Col(r, "项目编号");
            var dateStr = Col(r, "预测日期");
            if (Col(r, "状态") != "已冻结") continue;
            if (!versionIds.TryGetValue($"{projCode}|{dateStr}", out var vid)) continue;
            try { await _forecast.FreezeAsync(vid, ct); entries.Add(new("7-预测版本", $"{projCode}/{dateStr}", true, "已冻结")); }
            catch (Exception ex) { entries.Add(new("7-预测版本(冻结)", $"{projCode}/{dateStr}", false, ex.Message)); }
        }

        // 每个项目的经济摘要
        var summaries = new List<string>();
        foreach (var project in await _db.Projects.OrderBy(p => p.Code).ToListAsync(ct))
        {
            var e = await _facts.SummarizeProjectAsync(project.Id, ct);
            var f = await _forecast.GetSummaryAsync(project.Id, ct: ct);
            summaries.Add(
                $"[{project.Code} {project.Name}] 预计收入 {N(e.ExpectedIncome)} / 已知支出 {N(e.KnownExpenditure)}" +
                $" / 已收 {N(e.Received)} / 已付 {N(e.Paid)} / 资金压力 {N(e.FundingGap)}" +
                $" / 可分配 {N(e.AllocatableAmount)} / 已归属 {N(e.AllocatedAmount)} / 未归属 {N(e.UnallocatedAmount)}" +
                $" / 覆盖率 {(e.CoverageRate * 100m).ToString("0.0", CultureInfo.InvariantCulture)}%" +
                (f is null
                    ? " / 预测：尚无版本"
                    : $" / 预测 v{f.VersionNumber}({EconomicLabels.Confidence(f.Confidence)}) 目标 {N(f.TargetTotal)} 预计最终 {N(f.EstimatedFinalTotal)} 偏差 {N(f.VarianceTotal)} 未归属 {N(f.UnallocatedAmount)} 未纳入变化 {N(f.UnincludedChangeAmount)}"));
        }

        return new ImportReport(entries, summaries);
    }

    private static IEnumerable<Dictionary<string, string>> Read(string path)
    {
        if (!File.Exists(path)) yield break;
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) yield break;
        var headers = lines[0].Split('\t');
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = lines[i].Split('\t');
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < headers.Length; c++)
                row[headers[c].Trim()] = c < cells.Length ? cells[c].Trim() : "";
            yield return row;
        }
    }

    private static string Col(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var v) ? v : "";
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static decimal Money(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    private static decimal? MoneyOrNull(string s) => string.IsNullOrWhiteSpace(s) ? null : Money(s);
    private static DateOnly Date(string s) => DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
    private static string N(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture);
}
