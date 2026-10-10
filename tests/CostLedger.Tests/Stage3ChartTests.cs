using CostLedger.Domain;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace CostLedger.Tests;

/// <summary>
/// 阶段3 图表几何（纯函数）：柱状比例与零基线（含负值）、折线 X 等距、饼图角度和=360 且忽略负值、SVG 结构完整。
/// </summary>
public class Stage3ChartTests
{
    private static ChartModel Model(decimal a, decimal b, decimal c) => new()
    {
        Title = "测试",
        Categories = new[] { "甲", "乙", "丙" },
        Series = new ChartSeries[] { new("系列1", new[] { a, b, c }, "#CC785C") }
    };

    [Fact]
    public void 柱状_高度与数值成正比_零基线()
    {
        var (axes, bars) = ChartLayout.LayoutBar(Model(100, 200, 0), 760, 300);
        var v100 = bars.Single(x => x.Value == 100m);
        var v200 = bars.Single(x => x.Value == 200m);

        v200.H.Should().BeApproximately(v100.H * 2, 0.01);           // 高度∝数值
        axes.VMin.Should().Be(0m);                                    // 全正值基线为 0
        bars.All(b => b.Y + b.H <= axes.PlotY + axes.PlotH + 0.01).Should().BeTrue();
        // 类别内单系列：柱宽一致且在槽内居中
        bars.Select(b => b.W).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public void 柱状_负值向下_基线过零()
    {
        var (_, bars) = ChartLayout.LayoutBar(Model(100, -50, 0), 760, 300);
        var pos = bars.Single(x => x.Value == 100m);
        var neg = bars.Single(x => x.Value == -50m);

        neg.Y.Should().BeGreaterThan(pos.Y);                          // 负柱顶边在零线下方
        pos.Y.Should().BeLessThan(neg.Y + neg.H);                     // 正柱底边压在零线
        (pos.Y + pos.H).Should().BeApproximately(neg.Y, 0.01);        // 两柱共享零基线
    }

    [Fact]
    public void 折线_横坐标随类别等距()
    {
        var (_, pts) = ChartLayout.LayoutLine(Model(1, 2, 3), 760, 300);
        var xs = pts.OrderBy(p => p.Category).Select(p => p.X).ToList();
        (xs[1] - xs[0]).Should().BeApproximately(xs[2] - xs[1], 0.01);
    }

    [Fact]
    public void 饼图_角度和360_负值忽略_全负为空()
    {
        var slices = ChartLayout.LayoutPie(Model(100, -50, 300));
        slices.Sum(s => s.SweepAngle).Should().BeApproximately(360.0, 0.001);
        slices.Should().HaveCount(3);                                  // 负值占比 0 但块保留
        slices.Single(s => s.Label == "乙").Ratio.Should().Be(0);

        ChartLayout.LayoutPie(Model(-1, -2, -3)).Should().BeEmpty();   // 全负 → 空安全
    }

    [Fact]
    public void SVG_三种图均输出合法结构()
    {
        var m = Model(100, 200, 300);
        var bar = ChartSvg.Build(m, ChartKind.Bar);
        bar.Should().StartWith("<svg").And.EndWith("</svg>").And.Contain("<rect");

        var line = ChartSvg.Build(m, ChartKind.Line);
        line.Should().Contain("<path").And.Contain("<circle");

        var pie = ChartSvg.Build(m, ChartKind.Pie);
        pie.Should().Contain("A");                                     // 弧线指令

        // 转义：类别含 < & 不破坏结构
        var evil = new ChartModel { Categories = new[] { "a<b&c" }, Series = new[] { new ChartSeries("s", new[] { 1m }, "#000") } };
        ChartSvg.Build(evil, ChartKind.Bar).Should().Contain("&lt;b&amp;c").And.NotContain("a<b");
    }
}
