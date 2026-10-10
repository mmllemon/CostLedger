using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace CostLedger.App.Views;

/// <summary>经济模块视图的配色（与 Styles/Styles.axaml 的 vC 暖橙 token 一致）。
/// 用静态画刷而不依赖 FindResource，视图可在任意时机构建而不受资源加载顺序影响。</summary>
internal static class Palette
{
    public static readonly SolidColorBrush Bg = new(Color.Parse("#F4EEE5"));
    public static readonly SolidColorBrush Card = new(Color.Parse("#FFFDFA"));
    public static readonly SolidColorBrush Line = new(Color.Parse("#ECE3D6"));
    public static readonly SolidColorBrush Ink = new(Color.Parse("#211E1A"));
    public static readonly SolidColorBrush Txt2 = new(Color.Parse("#6B635A"));
    public static readonly SolidColorBrush Txt3 = new(Color.Parse("#A89E90"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#CC785C"));
    public static readonly SolidColorBrush AccentDeep = new(Color.Parse("#A24E30"));
    public static readonly SolidColorBrush AccentSoft = new(Color.Parse("#F0DFD2"));
    public static readonly SolidColorBrush Red = new(Color.Parse("#BE4A3C"));
    public static readonly SolidColorBrush White = new(Color.Parse("#FFFFFF"));
}

/// <summary>经济模块视图共用的轻量控件工厂（卡片 / 表格），减少重复布局代码。</summary>
internal static class Widgets
{
    public static Border Kpi(string label, string value, bool bad = false, double width = 200, string? desc = null)
    {
        var v = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = bad ? Palette.Red : Palette.Ink
        };
        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Txt3 },
                v
            }
        };
        if (!string.IsNullOrEmpty(desc))
            panel.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 11, Foreground = Palette.Txt3,
                TextWrapping = TextWrapping.Wrap, LineHeight = 1.5, Margin = new Thickness(0, 6, 0, 0)
            });
        return new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(15),
            Width = width,
            Margin = new Thickness(0, 0, 12, 12),
            Child = panel
        };
    }

    /// <summary>键值行（用于"数据质量/可解释程度"卡片），val 可标红。</summary>
    public static Control Field(string label, string value, bool bad = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 5, 0, 5) };
        var k = new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };
        var val = new TextBlock
        {
            Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = bad ? Palette.Red : Palette.Ink,
            HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(k, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(k);
        grid.Children.Add(val);
        return grid;
    }

    /// <summary>细进度条（0..1）。</summary>
    public static Control Track(double percent, bool warn = false)
    {
        percent = Math.Clamp(percent, 0, 1);
        var fill = new Border
        {
            Background = warn ? Palette.Red : Palette.Accent,
            CornerRadius = new CornerRadius(99),
            Height = 6
        };
        var track = new Grid
        {
            Height = 6,
            ColumnDefinitions = new ColumnDefinitions($"{percent * 100:0.##}*,{(1 - percent) * 100:0.##}*"),
            Margin = new Thickness(0, 6, 0, 6)
        };
        var bg = new Border { Background = new SolidColorBrush(Color.Parse("#ECE2D4")), CornerRadius = new CornerRadius(99), Height = 6 };
        Grid.SetColumnSpan(bg, 2);
        track.Children.Add(bg);
        Grid.SetColumn(fill, 0);
        track.Children.Add(fill);
        return track;
    }

    /// <summary>两张卡片并排（各占一半宽）。</summary>
    public static Control TwoCol(Control left, Control right)
    {
        var leftWrap = new Border { Child = left, Margin = new Thickness(0, 0, 7, 0) };
        var rightWrap = new Border { Child = right, Margin = new Thickness(7, 0, 0, 0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(leftWrap, 0);
        Grid.SetColumn(rightWrap, 1);
        grid.Children.Add(leftWrap);
        grid.Children.Add(rightWrap);
        return grid;
    }

    public static Border Card(string? title, Control body)
    {
        var panel = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(title))
            panel.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Txt3, LetterSpacing = 1.2 });
        panel.Children.Add(body);
        return new Border
        {
            Background = Palette.Card,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(20),
            Child = panel
        };
    }

    /// <summary>
    /// 二维表格：用"每行一个 Grid + 固定列宽"的纵向面板实现，比单个大 Grid 的 Auto 行更可靠。
    /// 表头带背景，数据行交替底色，末行为可选合计行。
    /// </summary>
    public static Control TableCore(string[] headers, System.Collections.Generic.IReadOnlyList<string[]> rows,
        string[]? footer, System.Collections.Generic.ISet<int>? rightAlign, System.Func<string[], bool>? redRow)
    {
        var cols = BuildColumns(headers, rightAlign);
        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(RowBorder(headers, cols, head: true, red: false, alt: false, bold: true, footerRow: false));
        for (int r = 0; r < rows.Count; r++)
        {
            var isRed = redRow?.Invoke(rows[r]) ?? false;
            panel.Children.Add(RowBorder(rows[r], cols, head: false, red: isRed, alt: r % 2 == 1, bold: false, footerRow: false));
        }
        if (footer != null)
            panel.Children.Add(RowBorder(footer, cols, head: false, red: false, alt: false, bold: true, footerRow: true));
        return panel;
    }

    private static ColumnDefinition[] BuildColumns(string[] headers, System.Collections.Generic.ISet<int>? rightAlign)
    {
        // 首列（名称）自适应占宽，其余等分剩余空间。
        var defs = new ColumnDefinition[headers.Length];
        defs[0] = new ColumnDefinition(GridLength.Star) { MinWidth = 120 };
        for (int c = 1; c < headers.Length; c++)
            defs[c] = new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 78 };
        return defs;
    }

    private static Border RowBorder(string[] cells, ColumnDefinition[] cols, bool head, bool red, bool alt, bool bold, bool footerRow)
    {
        var grid = new Grid();
        foreach (var cd in cols) grid.ColumnDefinitions.Add(new ColumnDefinition(cd.Width) { MinWidth = cd.MinWidth });

        for (int c = 0; c < cells.Length && c < cols.Length; c++)
        {
            var tb = new TextBlock
            {
                Text = c < cells.Length ? cells[c] : "",
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right
            };
            if (head) { tb.FontWeight = FontWeight.Bold; tb.Foreground = Palette.Txt3; }
            else if (bold) { tb.FontWeight = FontWeight.Bold; tb.Foreground = Palette.Ink; }
            else if (red) { tb.FontWeight = FontWeight.SemiBold; tb.Foreground = Palette.Red; }
            else { tb.Foreground = Palette.Ink; }
            Grid.SetColumn(tb, c);
            grid.Children.Add(tb);
        }

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(10, 8, 10, 8),
            Background = head ? new SolidColorBrush(Color.Parse("#F4EEE5")) : (alt ? new SolidColorBrush(Color.Parse("#FBF7F0")) : null),
            BorderBrush = Palette.Line,
            BorderThickness = footerRow ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1)
        };
        if (footerRow)
            border.Margin = new Thickness(0, 4, 0, 0);
        return border;
    }

    public static Control Table(string[] headers, System.Collections.Generic.IEnumerable<string[]> rows,
        System.Collections.Generic.ISet<int>? rightAlign = null, System.Func<string[], bool>? redRow = null)
        => TableCore(headers, new System.Collections.Generic.List<string[]>(rows), null, rightAlign, redRow);

    public static Control TableWithFooter(string[] headers, System.Collections.Generic.IEnumerable<string[]> rows,
        string[] footer, System.Collections.Generic.ISet<int>? rightAlign = null, System.Func<string[], bool>? redRow = null)
        => TableCore(headers, new System.Collections.Generic.List<string[]>(rows), footer, rightAlign, redRow);

    /// <summary>小型状态徽章（橙 / 红 / 灰 / 绿）。</summary>
    public static Border Badge(string text, string tone = "accent")
    {
        var (bg, fg) = tone switch
        {
            "danger" => (Palette.Red.Color, Palette.White.Color),
            "muted" => (Palette.Line.Color, Palette.Txt2.Color),
            "ok" => (Color.Parse("#E3EFE3"), Color.Parse("#3E7A44")),
            _ => (Palette.AccentSoft.Color, Palette.AccentDeep.Color)
        };
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }

    /// <summary>进度环：percent 0..1，中心显示文字。用 Ellipse + StrokeDashArray 可靠渲染。</summary>
    public static Control Ring(double percent, string centerText, double size = 96)
    {
        percent = Math.Clamp(percent, 0, 1);
        double stroke = 9;
        double r = size / 2 - stroke / 2 - 1;
        double circ = 2 * Math.PI * r;

        var track = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size, Height = size,
            Stroke = new SolidColorBrush(Color.Parse("#ECE2D4")),
            StrokeThickness = stroke,
            StrokeDashArray = new AvaloniaList<double> { 1, 0 }
        };
        var arc = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size, Height = size,
            Stroke = Palette.Accent,
            StrokeThickness = stroke,
            StrokeDashArray = new AvaloniaList<double> { circ * percent, circ * (1 - percent) },
            // 从 12 点方向起画
            RenderTransform = new RotateTransform(-90),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        var text = new TextBlock
        {
            Text = centerText, FontSize = 17, FontWeight = FontWeight.Bold,
            Foreground = Palette.AccentDeep, Width = size, TextAlignment = TextAlignment.Center
        };
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(track);
        grid.Children.Add(arc);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(text);
        return grid;
    }

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Palette.AccentDeep,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0)
    };

    public static TextBlock Error(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Palette.Red,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0)
    };

    public static LabeledControl Row(string label, Control control, double labelWidth = 84)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"), Margin = new Thickness(0, 4, 0, 4) };
        var tb = new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(tb);
        grid.Children.Add(control);
        return new LabeledControl(grid);
    }
}

/// <summary>包装一个字段行，便于加入表单 StackPanel。</summary>
internal sealed class LabeledControl : ContentControl
{
    public LabeledControl(Control content) => Content = content;
}

/// <summary>可由顶部全局项目切换器驱动的经济视图：切换项目时把选中项目传入并重载。</summary>
internal interface IProjectScoped
{
    void SelectProject(CostLedger.Domain.Project project);
}
