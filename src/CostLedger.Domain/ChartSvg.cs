using System.Globalization;
using System.Text;

namespace CostLedger.Domain;

/// <summary>
/// 阶段3 零依赖 SVG 图表导出：ChartModel → SVG 字符串（浏览器直接打开/打印/另存）。
/// 几何与屏显 ChartControl 共用 ChartLayout，两处渲染一致。
/// 不引第三方库：纯字符串拼接，样式内联。
/// </summary>
public static class ChartSvg
{
    private const string Ink = "#3d3a35";     // 主文字
    private const string Soft = "#8a8578";    // 次文字/网格

    public static string Build(ChartModel m, ChartKind kind, double w = 760, double h = 300)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" font-family=\"Microsoft YaHei, sans-serif\">");
        sb.Append($"<rect width=\"{w}\" height=\"{h}\" fill=\"#FFFDFA\"/>");
        sb.Append(Text(w / 2, 16, m.Title, 13, Ink, bold: true, anchor: "middle"));

        switch (kind)
        {
            case ChartKind.Pie: BuildPie(sb, m, w, h); break;
            case ChartKind.Line: BuildLine(sb, m, w, h); break;
            default: BuildBar(sb, m, w, h); break;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ---------- 柱状 / 折线（共用坐标轴） ----------
    private static void BuildBar(StringBuilder sb, ChartModel m, double w, double h)
    {
        var (axes, bars) = ChartLayout.LayoutBar(m, w, h);
        DrawGrid(sb, axes, m);
        foreach (var b in bars)
        {
            var color = m.Series[b.Series].Color;
            sb.Append($"<rect x=\"{F(b.X)}\" y=\"{F(b.Y)}\" width=\"{F(b.W)}\" height=\"{F(b.H)}\" fill=\"{color}\" rx=\"2\"/>");
        }
        DrawCategoryLabels(sb, m, axes);
        DrawLegend(sb, m, w);
    }

    private static void BuildLine(StringBuilder sb, ChartModel m, double w, double h)
    {
        var (axes, pts) = ChartLayout.LayoutLine(m, w, h);
        DrawGrid(sb, axes, m);

        for (int s = 0; s < m.Series.Count; s++)
        {
            var seg = pts.Where(p => p.Series == s).OrderBy(p => p.Category).ToList();
            if (seg.Count == 0) continue;
            var color = m.Series[s].Color;
            var d = string.Join(" ", seg.Select((p, i) => $"{(i == 0 ? "M" : "L")}{F(p.X)},{F(p.Y)}"));
            sb.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
            foreach (var p in seg)
                sb.Append($"<circle cx=\"{F(p.X)}\" cy=\"{F(p.Y)}\" r=\"3\" fill=\"#FFFDFA\" stroke=\"{color}\" stroke-width=\"2\"/>");
        }
        DrawCategoryLabels(sb, m, axes);
        DrawLegend(sb, m, w);
    }

    // ---------- 饼图 ----------
    private static void BuildPie(StringBuilder sb, ChartModel m, double w, double h)
    {
        var slices = ChartLayout.LayoutPie(m);
        double cx = w / 2, cy = h / 2 + 8, r = Math.Min(w, h) / 2 - 56;
        if (r < 20) r = Math.Min(w, h) / 2 - 24;

        foreach (var s in slices)
        {
            if (s.SweepAngle <= 0) continue;
            var color = m.Series[0].Color;
            // 多系列饼图暂不支持：取第一系列，若有多系列用类别序号取默认色板区分
            if (m.Categories.Count <= ChartModel.DefaultPalette.Length)
                color = ChartModel.DefaultPalette[s.Category % ChartModel.DefaultPalette.Length];
            var large = s.SweepAngle > 180 ? 1 : 0;
            var (x1, y1) = Polar(cx, cy, r, s.StartAngle);
            var (x2, y2) = Polar(cx, cy, r, s.StartAngle + s.SweepAngle);
            // 全圆（单块 100%）时弧线退化为同点，改画整圆
            if (s.SweepAngle >= 359.999)
                sb.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fill=\"{color}\"/>");
            else
                sb.Append($"<path d=\"M{F(cx)},{F(cy)} L{F(x1)},{F(y1)} A{F(r)},{F(r)} 0 {large} 1 {F(x2)},{F(y2)} Z\" fill=\"{color}\" stroke=\"#FFFDFA\" stroke-width=\"1.5\"/>");

            var mid = s.StartAngle + s.SweepAngle / 2;
            var (lx, ly) = Polar(cx, cy, r + 22, mid);
            sb.Append(Text(lx, ly, $"{Truncate(s.Label, 8)} {s.Ratio:P0}", 11, Ink, anchor: "middle"));
        }
        if (slices.Count == 0)
            sb.Append(Text(w / 2, h / 2, "（无可用正数值）", 12, Soft, anchor: "middle"));
    }

    // ---------- 公共件 ----------
    private static void DrawGrid(StringBuilder sb, ChartAxes a, ChartModel m)
    {
        // X 轴基线 + 水平网格线 + 刻度值
        double yBase = a.PlotY + a.PlotH;
        sb.Append($"<line x1=\"{F(a.PlotX)}\" y1=\"{F(yBase)}\" x2=\"{F(a.PlotX + a.PlotW)}\" y2=\"{F(yBase)}\" stroke=\"#d8d2c4\" stroke-width=\"1\"/>");
        foreach (var t in ChartLayout.Ticks(a.VMin, a.VMax))
        {
            double y = a.PlotY + a.PlotH * (double)((t - a.VMin) / (a.VMax - a.VMin));
            if (y < a.PlotY - 0.5 || y > yBase + 0.5) continue;
            sb.Append($"<line x1=\"{F(a.PlotX)}\" y1=\"{F(y)}\" x2=\"{F(a.PlotX + a.PlotW)}\" y2=\"{F(y)}\" stroke=\"#eee7d9\" stroke-width=\"1\"/>");
            sb.Append(Text(a.PlotX - 6, y + 4, t.ToString("N0"), 10.5, Soft, anchor: "end"));
        }
    }

    private static void DrawCategoryLabels(StringBuilder sb, ChartModel m, ChartAxes a)
    {
        int n = m.Categories.Count;
        if (n == 0) return;
        double slot = a.PlotW / n;
        for (int c = 0; c < n; c++)
            sb.Append(Text(a.PlotX + c * slot + slot / 2, a.PlotY + a.PlotH + 16, Truncate(m.Categories[c], 8), 11, Ink, anchor: "middle"));
    }

    private static void DrawLegend(StringBuilder sb, ChartModel m, double w)
    {
        double x = w - 18;
        for (int s = m.Series.Count - 1; s >= 0; s--)
        {
            var name = m.Series[s].Name;
            var tw = name.Length * 12.5 + 26;
            x -= tw;
            sb.Append($"<rect x=\"{F(x)}\" y=\"6\" width=\"12\" height=\"12\" fill=\"{m.Series[s].Color}\" rx=\"2\"/>");
            sb.Append(Text(x + 16, 16, name, 11, Ink));
        }
    }

    private static (double, double) Polar(double cx, double cy, double r, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        return (cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static string Text(double x, double y, string content, double size, string fill, bool bold = false, string? anchor = null)
        => $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"{F(size)}\" fill=\"{fill}\"{(bold ? " font-weight=\"bold\"" : "")}{(anchor is null ? "" : $" text-anchor=\"{anchor}\"")}>{Esc(content)}</text>";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
