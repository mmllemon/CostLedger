namespace CostLedger.Domain;

/// <summary>图表类型：柱状 / 折线 / 饼图（阶段3 零依赖图表的几何口径）。</summary>
public enum ChartKind { Bar, Line, Pie }

/// <summary>图表数据系列：名称 + 与 Categories 等长的数值 + 主题色（#RRGGBB）。</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<decimal> Values, string Color);

/// <summary>图表数据模型：标题 + 类别轴 + 一到多个系列。渲染器（Avalonia/SVG）共用同一几何。</summary>
public sealed class ChartModel
{
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ChartSeries> Series { get; init; } = Array.Empty<ChartSeries>();

    /// <summary>默认主题色（设计系统陶土橙系），按系列顺序取用。</summary>
    public static readonly string[] DefaultPalette = { "#CC785C", "#A24E30", "#8A8578", "#BE4A3C", "#6B6459" };
}

/// <summary>绘图区与值域（所有布局结果的公共参照系）。</summary>
public sealed record ChartAxes(double PlotX, double PlotY, double PlotW, double PlotH, decimal VMin, decimal VMax);

/// <summary>柱：所属系列/类别 + 外接矩形（Y 为顶边；负值柱顶边在基线下方）+ 原值。</summary>
public sealed record ChartBarRect(int Series, int Category, double X, double Y, double W, double H, decimal Value);

/// <summary>折线点：所属系列/类别 + 坐标 + 原值。</summary>
public sealed record ChartPoint(int Series, int Category, double X, double Y, decimal Value);

/// <summary>饼块：类别 + 原值 + 起始角/扫过角（度，12 点方向起顺时针）+ 占比。</summary>
public sealed record ChartSlice(int Category, string Label, decimal Value, double StartAngle, double SweepAngle, double Ratio);

/// <summary>
/// 阶段3 图表几何（纯函数，无 UI 依赖）：把 ChartModel 映射为坐标系内的矩形/点/扇形。
/// Avalonia 屏显（ChartControl）与 SVG 导出（ChartSvg）共用本几何，保证两处渲染一致、可单测。
/// 柱状支持负值（零基线上下分布）；饼图忽略负值（按 0 处理）且全负/全零时返回空。
/// </summary>
public static class ChartLayout
{
    public const double MarginLeft = 64, MarginRight = 16, MarginTop = 14, MarginBottom = 46;

    private static (double px, double py, double pw, double ph) Plot(double w, double h)
        => (MarginLeft, MarginTop, Math.Max(10, w - MarginLeft - MarginRight), Math.Max(10, h - MarginTop - MarginBottom));

    private static (decimal min, decimal max) Range(ChartModel m, bool forPie = false)
    {
        var vals = m.Series.SelectMany(s => forPie
            ? s.Values.Select(v => v > 0 ? v : 0m)
            : s.Values).ToList();
        if (vals.Count == 0) return (0, 1);
        var min = Math.Min(0, vals.Min());
        var max = vals.Max();
        if (min == max) max = min + 1;
        return (min, max);
    }

    /// <summary>柱状布局：每系列每类别一根柱（同类别并列）。</summary>
    public static (ChartAxes Axes, List<ChartBarRect> Bars) LayoutBar(ChartModel m, double w, double h)
    {
        var (px, py, pw, ph) = Plot(w, h);
        var (min, max) = Range(m);
        var axes = new ChartAxes(px, py, pw, ph, min, max);
        var bars = new List<ChartBarRect>();
        int nCat = m.Categories.Count, nSer = m.Series.Count;
        if (nCat == 0 || nSer == 0) return (axes, bars);

        decimal Span(decimal v) => (v - min) / (max - min);
        double Y(decimal v) => py + ph * (1 - (double)Span(v));

        double slot = pw / nCat;
        double groupW = slot * 0.7;
        double barW = groupW / nSer;

        for (int s = 0; s < nSer; s++)
        {
            var vals = m.Series[s].Values;
            for (int c = 0; c < nCat && c < vals.Count; c++)
            {
                var v = vals[c];
                double top = Y(Math.Max(v, 0m));
                double bottom = Y(Math.Min(v, 0m));
                bars.Add(new ChartBarRect(s, c,
                    X: px + c * slot + (slot - groupW) / 2 + s * barW,   // 组内并列、整组在类别槽内居中
                    Y: top,
                    W: barW * 0.9,
                    H: Math.Max(0, bottom - top),   // 零值柱高度 0（不可见），不越出基线
                    Value: v));
            }
        }
        return (axes, bars);
    }

    /// <summary>折线布局：每系列一条折线，点在类别中心。</summary>
    public static (ChartAxes Axes, List<ChartPoint> Points) LayoutLine(ChartModel m, double w, double h)
    {
        var (px, py, pw, ph) = Plot(w, h);
        var (min, max) = Range(m);
        var axes = new ChartAxes(px, py, pw, ph, min, max);
        var pts = new List<ChartPoint>();
        int nCat = m.Categories.Count;
        if (nCat == 0 || m.Series.Count == 0) return (axes, pts);

        double step = nCat == 1 ? 0 : pw / (nCat - 1);
        decimal Span(decimal v) => (v - min) / (max - min);
        double Y(decimal v) => py + ph * (1 - (double)Span(v));

        for (int s = 0; s < m.Series.Count; s++)
        {
            var vals = m.Series[s].Values;
            for (int c = 0; c < nCat && c < vals.Count; c++)
                pts.Add(new ChartPoint(s, c, px + c * step, Y(vals[c]), vals[c]));
        }
        return (axes, pts);
    }

    /// <summary>饼图布局：只取第一个系列；负值按 0 处理；合计为 0 返回空列表。</summary>
    public static List<ChartSlice> LayoutPie(ChartModel m)
    {
        var slices = new List<ChartSlice>();
        if (m.Series.Count == 0 || m.Categories.Count == 0) return slices;

        var vals = m.Series[0].Values;
        decimal total = 0;
        for (int c = 0; c < m.Categories.Count && c < vals.Count; c++)
            total += Math.Max(0, vals[c]);
        if (total <= 0) return slices;

        double angle = -90;   // 12 点方向起
        for (int c = 0; c < m.Categories.Count && c < vals.Count; c++)
        {
            var v = Math.Max(0, vals[c]);
            var ratio = (double)(v / total);
            var sweep = ratio * 360.0;
            slices.Add(new ChartSlice(c, m.Categories[c], v, angle, sweep, ratio));
            angle += sweep;
        }
        return slices;
    }

    /// <summary>值轴刻度：从 min 到 max 均分 ticks 份（含端点）。</summary>
    public static List<decimal> Ticks(decimal min, decimal max, int ticks = 4)
    {
        var list = new List<decimal>();
        for (int i = 0; i <= ticks; i++)
            list.Add(min + (max - min) * i / ticks);
        return list;
    }
}
