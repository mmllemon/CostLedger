using CostLedger.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CostLedger.App;

/// <summary>
/// 正式版式打印：把 LedgerResult 渲染为 A4 可打印 HTML（页眉/表头/合计/签章栏），
/// 用系统默认浏览器打开即可打印或"另存为 PDF"。
/// 说明：这是"打印版式"的零依赖过渡方案；规范 §2 约定复杂版式最终归 M5 专业报表引擎。
/// </summary>
public static class PrintService
{
    public static string BuildHtml(string projectName, LedgerResult ledger, DateTime printedAt)
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
.bar { text-align:center; margin:14px 0; }
.bar button { padding:8px 24px; font-size:13px; cursor:pointer; }
@media print { .bar { display:none; } }
</style></head><body>");

        sb.Append("<div class=\"bar\"><button onclick=\"window.print()\">打印 / 另存为 PDF</button></div>");
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
        sb.Append("<div class=\"foot\"><div>编制：<span></span></div><div>审核：<span></span></div><div>日期：<span></span></div></div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>写临时 HTML 并用系统默认浏览器打开（用户在其中打印或另存 PDF）。</summary>
    public static void OpenInBrowser(string html, string fileNameHint)
    {
        var path = Path.Combine(Path.GetTempPath(), $"CostLedger_{Sanitize(fileNameHint)}_{DateTime.Now:HHmmss}.html");
        File.WriteAllText(path, html, Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private static string Esc(string? s) => string.IsNullOrEmpty(s)
        ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
