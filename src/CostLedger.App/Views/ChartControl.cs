using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using System;
using System.Globalization;
using System.Linq;

namespace CostLedger.App.Views;

/// <summary>
/// 阶段3 零依赖屏显图表：ChartModel → Avalonia Shapes 画布（柱/折线/饼）。
/// 几何与 SVG 导出共用 ChartLayout，两处渲染一致；不引第三方图表库。
/// 固定设计尺寸（默认 760×300），由调用方放入 ScrollViewer 适配卡片宽度。
/// </summary>
public static class ChartControl
{
    private const string Ink = "#3d3a35";
    private const string Soft = "#8a8578";
    private const string GridLine = "#eee7d9";
    private const string Axis = "#d8d2c4";

    public static Canvas Build(ChartModel m, ChartKind kind, double w = 760, double h = 300)
    {
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(Text(w / 2, 4, m.Title, 13, Ink, bold: true, centered: true));

        switch (kind)
        {
            case ChartKind.Pie: BuildPie(canvas, m, w, h); break;
            case ChartKind.Line: BuildLine(canvas, m, w, h); break;
            default: BuildBar(canvas, m, w, h); break;
        }
        return canvas;
    }

    // ---------- 柱状 ----------
    private static void BuildBar(Canvas c, ChartModel m, double w, double h)
    {
        var (axes, bars) = ChartLayout.LayoutBar(m, w, h);
        DrawGrid(c, axes);

        foreach (var b in bars)
        {
            if (b.H <= 0) continue;
            c.Children.Add(new Rectangle
            {
                Width = b.W, Height = b.H,
                Fill = Brush(m.Series[b.Series].Color),
                RadiusX = 2, RadiusY = 2,
                [Canvas.LeftProperty] = b.X, [Canvas.TopProperty] = b.Y
            });
        }
        DrawCategoryLabels(c, m, axes);
        DrawLegend(c, m, w);
    }

    // ---------- 折线 ----------
    private static void BuildLine(Canvas c, ChartModel m, double w, double h)
    {
        var (axes, pts) = ChartLayout.LayoutLine(m, w, h);
        DrawGrid(c, axes);

        for (int s = 0; s < m.Series.Count; s++)
        {
            var seg = pts.Where(p => p.Series == s).OrderBy(p => p.Category).ToList();
            if (seg.Count == 0) continue;
            var color = Brush(m.Series[s].Color);

            c.Children.Add(new Polyline
            {
                Points = new Points(seg.Select(p => new Point(p.X, p.Y))),
                Stroke = color, StrokeThickness = 2
            });
            foreach (var p in seg)
                c.Children.Add(new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = Brushes.Transparent, Stroke = color, StrokeThickness = 2,
                    [Canvas.LeftProperty] = p.X - 3, [Canvas.TopProperty] = p.Y - 3
                });
        }
        DrawCategoryLabels(c, m, axes);
        DrawLegend(c, m, w);
    }

    // ---------- 饼图 ----------
    private static void BuildPie(Canvas c, ChartModel m, double w, double h)
    {
        var slices = ChartLayout.LayoutPie(m);
        double cx = w / 2, cy = h / 2 + 8, r = Math.Min(w, h) / 2 - 56;
        if (r < 20) r = Math.Min(w, h) / 2 - 24;

        foreach (var s in slices)
        {
            if (s.SweepAngle <= 0) continue;
            var color = Brush(ChartModel.DefaultPalette[s.Category % ChartModel.DefaultPalette.Length]);
            var large = s.SweepAngle > 180;

            if (s.SweepAngle >= 359.999)
            {
                c.Children.Add(new Ellipse
                {
                    Width = r * 2, Height = r * 2, Fill = color,
                    [Canvas.LeftProperty] = cx - r, [Canvas.TopProperty] = cy - r
                });
            }
            else
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    var (x1, y1) = Polar(cx, cy, r, s.StartAngle);
                    var (x2, y2) = Polar(cx, cy, r, s.StartAngle + s.SweepAngle);
                    ctx.BeginFigure(new Point(cx, cy), isFilled: true);
                    ctx.LineTo(new Point(x1, y1));
                    ctx.ArcTo(new Point(x2, y2), new Size(r, r), 0, large, SweepDirection.Clockwise);
                    ctx.EndFigure(true);
                }
                c.Children.Add(new Avalonia.Controls.Shapes.Path { Data = geo, Fill = color, Stroke = Brushes.Transparent, StrokeThickness = 1.5 });
            }

            var mid = s.StartAngle + s.SweepAngle / 2;
            var (lx, ly) = Polar(cx, cy, r + 22, mid);
            c.Children.Add(Text(lx, ly, $"{Trunc(s.Label, 8)} {s.Ratio:P0}", 11, Ink, centered: true));
        }

        if (slices.Count == 0)
            c.Children.Add(Text(w / 2, h / 2, "（无可用正数值）", 12, Soft, centered: true));
    }

    // ---------- 公共件 ----------
    private static void DrawGrid(Canvas c, ChartAxes a)
    {
        double yBase = a.PlotY + a.PlotH;
        c.Children.Add(Line(a.PlotX, yBase, a.PlotX + a.PlotW, yBase, Axis));
        foreach (var t in ChartLayout.Ticks(a.VMin, a.VMax))
        {
            double y = a.PlotY + a.PlotH * (double)((t - a.VMin) / (a.VMax - a.VMin));
            if (y < a.PlotY - 0.5 || y > yBase + 0.5) continue;
            c.Children.Add(Line(a.PlotX, y, a.PlotX + a.PlotW, y, GridLine));
            c.Children.Add(Text(a.PlotX - 6, y - 7, t.ToString("N0", CultureInfo.CurrentCulture), 10.5, Soft, right: true));
        }
    }

    private static void DrawCategoryLabels(Canvas c, ChartModel m, ChartAxes a)
    {
        int n = m.Categories.Count;
        if (n == 0) return;
        double slot = a.PlotW / n;
        for (int i = 0; i < n; i++)
            c.Children.Add(Text(a.PlotX + i * slot + slot / 2, a.PlotY + a.PlotH + 6, Trunc(m.Categories[i], 8), 11, Ink, centered: true));
    }

    private static void DrawLegend(Canvas c, ChartModel m, double w)
    {
        // 从右往左排：色块 12×12 + 文本
        double x = w - 18;
        for (int s = m.Series.Count - 1; s >= 0; s--)
        {
            var name = m.Series[s].Name;
            var textW = name.Length * 12.0 + 8;
            x -= textW;
            var tb = Text(x + 16, 8, name, 11, Ink);
            c.Children.Add(tb);
            x -= 20;
            c.Children.Add(new Rectangle
            {
                Width = 12, Height = 12,
                Fill = Brush(m.Series[s].Color), RadiusX = 2, RadiusY = 2,
                [Canvas.LeftProperty] = x, [Canvas.TopProperty] = 8
            });
        }
    }

    private static Line Line(double x1, double y1, double x2, double y2, string color) => new()
    {
        StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2),
        Stroke = Brush(color), StrokeThickness = 1
    };

    private static TextBlock Text(double x, double y, string content, double size, string color,
        bool bold = false, bool centered = false, bool right = false)
    {
        var tb = new TextBlock
        {
            Text = content,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal
        };
        // Canvas 定位：centered/right 依赖文本宽度，按中文字符≈字号宽度估算后平移（屏显近似，SVG 端为原生 text-anchor）
        double estW = content.Length * size;
        double dx = centered ? -estW / 2 : right ? -estW : 0;
        if (dx != 0) tb.RenderTransform = new TranslateTransform(dx, 0);
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        return tb;
    }

    private static TranslateTransform Translate(double x, double y) => new(x, y);

    private static (double, double) Polar(double cx, double cy, double r, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        return (cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
