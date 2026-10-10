using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
    public static Border Kpi(string label, string value, bool bad = false, double width = 200)
    {
        var v = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = bad ? Palette.Red : Palette.Ink
        };
        return new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(15),
            Width = width,
            Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 11, Foreground = Palette.Txt3 },
                    v
                }
            }
        };
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

    /// <summary>二维表格：首行表头，其余数据行。rightAlign 指定右对齐列；redRow 判定某行是否标红。</summary>
    public static Grid Table(string[] headers, System.Collections.Generic.IEnumerable<string[]> rows,
        System.Collections.Generic.ISet<int>? rightAlign = null, System.Func<string[], bool>? redRow = null)
    {
        var list = new System.Collections.Generic.List<string[]>(rows);
        var grid = new Grid();
        for (int c = 0; c < headers.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 70 });
        for (int r = 0; r < list.Count + 1; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < headers.Length; c++)
            grid.Children.Add(Cell(headers[c], 0, c, head: true, red: false, right: false));
        for (int r = 0; r < list.Count; r++)
        {
            var isRed = redRow?.Invoke(list[r]) ?? false;
            for (int c = 0; c < headers.Length; c++)
            {
                var right = rightAlign?.Contains(c) ?? false;
                grid.Children.Add(Cell(list[r][c], r + 1, c, head: false, red: isRed, right: right));
            }
        }
        return grid;
    }

    private static TextBlock Cell(string text, int row, int col, bool head, bool red, bool right)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 24, 7),
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            TextWrapping = TextWrapping.NoWrap
        };
        if (head)
        {
            tb.FontWeight = FontWeight.Bold;
            tb.Foreground = Palette.Txt3;
        }
        else if (red)
        {
            tb.FontWeight = FontWeight.SemiBold;
            tb.Foreground = Palette.Red;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
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
