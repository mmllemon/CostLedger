using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CostLedger.Infrastructure;

/// <summary>
/// 极简 CSV 读写（零依赖）。Excel 可直接打开/另存 CSV，满足批量导入导出。
/// 写入用 UTF-8 BOM 由调用方补，保证 Excel 中文不乱码。
/// </summary>
public static class CsvUtil
{
    public static string Write(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", headers.Select(Escape))).Append("\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", r.Select(Escape))).Append("\r\n");
        return sb.ToString();
    }

    private static string Escape(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    /// <summary>解析 CSV 文本为行（支持引号包裹、字段内逗号/换行、"" 转义）。</summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var record = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { /* skip */ }
                else if (c == '\n')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    rows.Add(record.ToArray());
                    record.Clear();
                }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            rows.Add(record.ToArray());
        }
        return rows.Where(r => r.Length > 1 || (r.Length == 1 && r[0].Trim().Length > 0)).ToList();
    }
}
