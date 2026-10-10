using System;
using System.Text;

namespace CostLedger.Infrastructure;

/// <summary>
/// A4 内部汇报报表版式渲染（零依赖 HTML）。纯函数，置于基础设施层以便单测覆盖：
/// 页眉公司名、表头、数据行、合计、口径声明、签章栏。用系统浏览器打印或另存 PDF。
/// 口径声明（caliberNote）是成本岗报表的必备要素之一——它把"未付/未收（推算）"
/// 与财务应付/应收账款结论区分开，缺省则不插入。
/// </summary>
public static class A4ReportLayout
{
    public static string Render(string projectName, LedgerResult ledger, DateTime printedAt, string? company = null, string? caliberNote = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<title>").Append(Esc(ledger.Title)).Append("</title>");
        sb.Append(@"<style>
@page { size: A4; margin: 18mm 16mm; }
* { box-sizing: border-box; }
body { font-family: 'SimSun','宋体','Microsoft YaHei',serif; color:#1a1a1a; margin:0; }
.hd { display:flex; justify-content:space-between; align-items:flex-end; border-bottom:2px solid #333; padding-bottom:6px; margin-bottom:10px; }
.hd .t { font-size:20px; font-weight:700; letter-spacing:2px; }
.hd .s { font-size:12px; color:#555; }
table { width:100%; border-collapse:collapse; font-size:12px; }
th,td { border:1px solid #999; padding:5px 7px; }
th { background:#f0f0f0; font-weight:700; text-align:center; }
td.num { text-align:right; }
tfoot td { font-weight:700; background:#fafafa; }
.foot { display:flex; justify-content:space-between; margin-top:26px; font-size:12px; }
.foot span { border-bottom:1px solid #333; min-width:160px; display:inline-block; }
.note { margin-top:10px; font-size:11px; line-height:1.7; color:#444; border-left:3px solid #999; padding-left:8px; }
.bar { text-align:center; margin:14px 0; }
.bar button { padding:8px 24px; font-size:13px; cursor:pointer; }
@media print { .bar { display:none; } }
</style></head><body>");

        sb.Append("<div class=\"bar\"><button onclick=\"window.print()\">打印 / 另存为 PDF</button></div>");
        if (!string.IsNullOrWhiteSpace(company))
            sb.Append("<div style=\"text-align:center;font-size:13px;font-weight:700;letter-spacing:1px;\">")
              .Append(Esc(company)).Append("</div>");
        sb.Append("<div class=\"hd\"><div class=\"t\">").Append(Esc(ledger.Title)).Append("</div>");
        sb.Append("<div class=\"s\">项目：").Append(Esc(projectName)).Append("　打印日期：")
          .Append(printedAt.ToString("yyyy-MM-dd")).Append("</div></div>");

        sb.Append("<table><thead><tr>");
        foreach (var h in ledger.Headers) sb.Append("<th>").Append(Esc(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in ledger.Rows)
        {
            sb.Append("<tr>");
            foreach (var c in row) sb.Append("<td>").Append(Esc(c)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody>");

        if (ledger.Footers.Count > 0)
        {
            sb.Append("<tfoot>");
            foreach (var f in ledger.Footers)
            {
                sb.Append("<tr>");
                foreach (var c in f) sb.Append("<td>").Append(Esc(c)).Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tfoot>");
        }

        sb.Append("</table>");
        if (!string.IsNullOrWhiteSpace(caliberNote))
            sb.Append("<div class=\"note\">").Append(Esc(caliberNote)).Append("</div>");
        sb.Append("<div class=\"foot\"><div>编制：<span></span></div><div>审核：<span></span></div><div>日期：<span></span></div></div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Esc(string? s) => string.IsNullOrEmpty(s)
        ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
