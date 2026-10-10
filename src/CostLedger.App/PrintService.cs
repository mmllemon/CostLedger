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
    /// <param name="caliberNote">口径说明（可选）。内部汇报报表必须写明数据性质与责任边界，
    /// 避免"未付/未收"被理解成财务应付应收结论。版式渲染下沉 Infrastructure 的 A4ReportLayout 以便单测。</param>
    public static string BuildHtml(string projectName, LedgerResult ledger, DateTime printedAt, string? company = null, string? caliberNote = null)
        => A4ReportLayout.Render(projectName, ledger, printedAt, company, caliberNote);

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
}
