using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>报表运行结果（已格式化，便于直接渲染表格）</summary>
public record ReportResult(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string[]> Rows,
    IReadOnlyList<string> Footers);

/// <summary>
/// M2-C 报表引擎：报表定义 CRUD + 运行（筛选 → 投影 → 格式化 → 聚合）。
/// 仅读业务数据做展示，绝不写回、不参与成本脊柱算账（铁律）。
/// </summary>
public class ReportService
{
    private readonly AppDbContext _db;
    public ReportService(AppDbContext db) => _db = db;

    // ---------- 定义 CRUD ----------
    public Task<List<ReportDef>> ListReportsAsync()
        => _db.ReportDefs.Include(r => r.Columns).Include(r => r.Filters)
            .OrderBy(r => r.Name).ToListAsync();

    /// <summary>新增或替换保存（子表整体替换，避免增量对账复杂度）。</summary>
    public async Task<int> SaveReportAsync(ReportDef def)
    {
        if (def.Id == 0)
        {
            _db.ReportDefs.Add(def);
            await _db.SaveChangesAsync();
            return def.Id;
        }

        var existing = await _db.ReportDefs
            .Include(r => r.Columns).Include(r => r.Filters)
            .FirstAsync(r => r.Id == def.Id);
        existing.Code = def.Code;
        existing.Name = def.Name;
        existing.Title = def.Title;
        existing.DataSourceCode = def.DataSourceCode;
        existing.Description = def.Description;

        _db.ReportColumns.RemoveRange(existing.Columns);
        _db.ReportFilters.RemoveRange(existing.Filters);
        await _db.SaveChangesAsync();

        foreach (var c in def.Columns) { c.Id = 0; c.ReportDefId = def.Id; _db.ReportColumns.Add(c); }
        foreach (var f in def.Filters) { f.Id = 0; f.ReportDefId = def.Id; _db.ReportFilters.Add(f); }
        await _db.SaveChangesAsync();
        return def.Id;
    }

    public async Task DeleteReportAsync(int id)
    {
        var def = await _db.ReportDefs
            .Include(r => r.Columns).Include(r => r.Filters)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (def is null) return;
        _db.ReportDefs.Remove(def);
        await _db.SaveChangesAsync();
    }

    // ---------- 运行 ----------
    public ReportResult Run(ReportDef def)
    {
        var ds = ReportDataSources.Get(def.DataSourceCode);
        if (ds is null)
            return new ReportResult(def.Title, Array.Empty<string>(), Array.Empty<string[]>(), Array.Empty<string>());

        var rows = ds.Query(_db).ToList();

        foreach (var filter in def.Filters.OrderBy(f => f.DisplayOrder))
            rows = rows.Where(o => Match(o, ds, filter)).ToList();

        var cols = def.Columns.Where(c => c.Visible).OrderBy(c => c.DisplayOrder).ToList();
        var headers = cols.Select(c => string.IsNullOrWhiteSpace(c.Label)
            ? ds.Field(c.FieldKey)?.Label ?? c.FieldKey : c.Label).ToArray();

        var body = rows.Select(o => cols.Select(c => Format(o, ds, c)).ToArray()).ToList();
        var footer = BuildFooter(rows, ds, cols);
        return new ReportResult(def.Title, headers, body, footer);
    }

    private static bool Match(object obj, ReportDataSource ds, ReportFilter f)
    {
        var field = ds.Field(f.FieldKey);
        if (field is null) return true;
        var raw = PropValue(obj, f.FieldKey);
        var kind = field.Kind;
        var left = ToText(raw, kind);

        switch (kind)
        {
            case ReportValueKind.Number:
            case ReportValueKind.Money:
                if (!decimal.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var ln)) return false;
                if (!decimal.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var rn)) return false;
                return f.Operator switch
                {
                    FilterOperator.Equals => ln == rn,
                    FilterOperator.NotEquals => ln != rn,
                    FilterOperator.GreaterThan => ln > rn,
                    FilterOperator.LessThan => ln < rn,
                    FilterOperator.GreaterOrEqual => ln >= rn,
                    FilterOperator.LessOrEqual => ln <= rn,
                    _ => left.Contains(f.Value, StringComparison.OrdinalIgnoreCase)
                };
            case ReportValueKind.Date:
                if (!DateOnly.TryParse(left, out var ld)) return false;
                if (!DateOnly.TryParse(f.Value, out var rd)) return false;
                var cmp = ld.CompareTo(rd);
                return f.Operator switch
                {
                    FilterOperator.Equals => cmp == 0,
                    FilterOperator.NotEquals => cmp != 0,
                    FilterOperator.GreaterThan => cmp > 0,
                    FilterOperator.LessThan => cmp < 0,
                    FilterOperator.GreaterOrEqual => cmp >= 0,
                    FilterOperator.LessOrEqual => cmp <= 0,
                    _ => left.Contains(f.Value, StringComparison.OrdinalIgnoreCase)
                };
            default:
                return f.Operator switch
                {
                    FilterOperator.Equals => string.Equals(left, f.Value, StringComparison.OrdinalIgnoreCase),
                    FilterOperator.NotEquals => !string.Equals(left, f.Value, StringComparison.OrdinalIgnoreCase),
                    _ => left.Contains(f.Value, StringComparison.OrdinalIgnoreCase)
                };
        }
    }

    private static string Format(object obj, ReportDataSource ds, ReportColumn col)
    {
        var field = ds.Field(col.FieldKey);
        var kind = field?.Kind ?? ReportValueKind.Text;
        var raw = PropValue(obj, col.FieldKey);
        if (raw is null) return "";

        switch (kind)
        {
            case ReportValueKind.Money:
            case ReportValueKind.Number:
                var num = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
                return num.ToString(col.Format ?? field?.Format ?? "0.##", CultureInfo.CurrentCulture);
            case ReportValueKind.Date:
                var d = raw is DateOnly dd ? dd : DateOnly.FromDateTime(Convert.ToDateTime(raw));
                return d.ToString(col.Format ?? field?.Format ?? "yyyy-MM-dd");
            case ReportValueKind.Bool:
                return Convert.ToBoolean(raw) ? "是" : "否";
            default:
                return raw.ToString() ?? "";
        }
    }

    private static string[] BuildFooter(List<object> rows, ReportDataSource ds, List<ReportColumn> cols)
    {
        var footer = new string[cols.Count];
        for (int i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            if (col.Aggregate == AggregateType.None) { footer[i] = ""; continue; }
            var field = ds.Field(col.FieldKey);
            var nums = rows.Select(o => PropValue(o, col.FieldKey))
                .Where(v => v is not null)
                .Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture))
                .ToList();
            decimal? result = col.Aggregate switch
            {
                AggregateType.Sum => nums.Count > 0 ? nums.Sum() : 0m,
                AggregateType.Count => rows.Count,
                AggregateType.Avg => nums.Count > 0 ? nums.Average() : (decimal?)null,
                AggregateType.Min => nums.Count > 0 ? nums.Min() : (decimal?)null,
                AggregateType.Max => nums.Count > 0 ? nums.Max() : (decimal?)null,
                _ => null
            };
            footer[i] = result.HasValue
                ? result.Value.ToString(col.Format ?? field?.Format ?? "N2", CultureInfo.CurrentCulture)
                : "";
        }
        return footer;
    }

    private static object? PropValue(object obj, string key)
        => obj.GetType().GetProperty(key)?.GetValue(obj);

    private static string ToText(object? raw, ReportValueKind kind) => raw switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        _ => raw.ToString() ?? ""
    };
}
