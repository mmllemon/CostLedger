using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    public static readonly SolidColorBrush Txt3 = new(Color.Parse("#8A8075"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#CC785C"));
    public static readonly SolidColorBrush AccentDeep = new(Color.Parse("#A24E30"));
    public static readonly SolidColorBrush AccentSoft = new(Color.Parse("#F0DFD2"));
    public static readonly SolidColorBrush Red = new(Color.Parse("#BE4A3C"));
    public static readonly SolidColorBrush White = new(Color.Parse("#FFFFFF"));

    /// <summary>
    /// 按资源名取画刷的安全入口：不依赖控件是否已挂到可视树，也不会因查找失败抛异常。
    /// 历史代码里的 (SolidColorBrush)FindResource(...) 在 View 早期渲染时会拿到 UnsetValue
    /// 而抛 InvalidCastException，导致整个程序崩溃——统一改用本方法。
    /// </summary>
    public static SolidColorBrush Find(string key) => key switch
    {
        "BgBrush" => Bg,
        "CardBrush" => Card,
        "LineBrush" => Line,
        "InkBrush" => Ink,
        "Txt2Brush" => Txt2,
        "Txt3Brush" => Txt3,
        "AccentBrush" => Accent,
        "AccentDeepBrush" => AccentDeep,
        "AccentSoftBrush" => AccentSoft,
        "RedBrush" => Red,
        "RedSoftBrush" => new SolidColorBrush(Color.Parse("#F6E2DD")),
        "TrackBrush" => new SolidColorBrush(Color.Parse("#ECE2D4")),
        "BarBrush" => new SolidColorBrush(Color.Parse("#E7DAC9")),
        "WhiteBrush" => White,
        _ => Ink
    };
}

/// <summary>经济模块视图共用的轻量控件工厂（卡片 / 表格），减少重复布局代码。</summary>
internal static class Widgets
{
    public static Border Kpi(string label, string value, bool bad = false, double width = 0, string? desc = null)
    {
        var v = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = bad ? Palette.Red : Palette.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis
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
                TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 6, 0, 0)
            });
        var border = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 12, 12),
            Child = panel
        };
        // width>0 时固定宽（少数并排场景）；否则由容器（KpiGrid）控制，铺满等分格。
        if (width > 0) border.Width = width;
        return border;
    }

    /// <summary>KPI 网格：固定每行 4 列，避免 WrapPanel 在不同宽度下参差换行。</summary>
    public static UniformGrid KpiGrid(int columns = 4)
        => new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, -12, -12) };

    /// <summary>把 KPI 依次放进 4 列网格（每个卡片自带右下外边距，网格用负外边距抵消）。</summary>
    public static void FillKpis(UniformGrid grid, System.Collections.Generic.IEnumerable<Border> kpis)
    {
        foreach (var k in kpis) grid.Children.Add(k);
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
        var align = rightAlign ?? new System.Collections.Generic.HashSet<int>();
        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(RowBorder(headers, cols, align, head: true, red: false, alt: false, bold: true, footerRow: false));

        if (rows.Count == 0 && footer == null)
        {
            panel.Children.Add(EmptyState("暂无数据"));
            return panel;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var isRed = redRow?.Invoke(rows[r]) ?? false;
            panel.Children.Add(RowBorder(rows[r], cols, align, head: false, red: isRed, alt: r % 2 == 1, bold: false, footerRow: false));
        }
        if (footer != null)
            panel.Children.Add(RowBorder(footer, cols, align, head: false, red: false, alt: false, bold: true, footerRow: true));
        return panel;
    }

    /// <summary>空态占位行。</summary>
    public static Control EmptyState(string text) => new Border
    {
        Padding = new Thickness(10, 22, 10, 22),
        BorderBrush = Palette.Line,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Child = new TextBlock { Text = text, FontSize = 12.5, Foreground = Palette.Txt3, HorizontalAlignment = HorizontalAlignment.Center }
    };

    private static ColumnDefinition[] BuildColumns(string[] headers, System.Collections.Generic.ISet<int>? rightAlign)
    {
        // 首列（名称）自适应占宽，其余等分剩余空间；最小宽度保证不重叠。
        var defs = new ColumnDefinition[headers.Length];
        defs[0] = new ColumnDefinition(GridLength.Star) { MinWidth = 130 };
        for (int c = 1; c < headers.Length; c++)
            defs[c] = new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 80 };
        return defs;
    }

    private static Border RowBorder(string[] cells, ColumnDefinition[] cols, System.Collections.Generic.ISet<int> align, bool head, bool red, bool alt, bool bold, bool footerRow)
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                // 首列左对齐；其余按 rightAlign 集合决定，不再一律右对齐。
                TextAlignment = c == 0 ? TextAlignment.Left : (align.Contains(c) ? TextAlignment.Right : TextAlignment.Left)
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

    /// <summary>
    /// 页面顶栏：左侧圆形头像 + 标题 + 副标题，右侧操作按钮；对应预览稿的 .topbar/.proj。
    /// </summary>
    public static Control PageHeader(string avatar, string title, string subtitle, params Control[] actions)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Palette.Ink });
        if (!string.IsNullOrEmpty(subtitle))
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = Palette.Txt3 });

        var ava = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
            Background = Palette.AccentDeep,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = avatar, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Palette.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ava);
        left.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) if (a != null) right.Children.Add(a);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static Button ActionButton(string text, Action onClick)
    {
        var b = new Button { Content = text };
        b.Classes.Add("pill");
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>页头主按钮（强调色），用于页面最重要的那个动作。</summary>
    public static Button PrimaryActionButton(string text, Action onClick)
    {
        var b = new Button { Content = text };
        b.Classes.Add("primary");
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>
    /// 以半透明遮罩 + 居中卡片的形式打开模态表单（挂在宿主窗口根层，不新开窗口）。
    /// 提交委托返回 null 表示成功（关闭），返回中文文案表示失败（卡片内显示）。
    /// </summary>
    public static void OpenForm(Visual anchor, string title, string subtitle, Control body,
        string primaryText, Func<Task<string?>> submit)
    {
        var owner = TopLevel.GetTopLevel(anchor) as Window;
        if (owner?.Content is not Control root) return;

        if (owner.Content is not Grid host)
        {
            // 把原内容包进 Grid，便于叠加遮罩层
            owner.Content = null;
            host = new Grid();
            host.Children.Add(root);
            owner.Content = host;
        }

        var overlay = FormDialog.Build(title, subtitle, body, primaryText, submit, () =>
        {
            host.Children.Remove(host.Children[^1]);
        });
        // 宿主 Grid 有行列定义时，叠加层必须跨满整张网格，否则会缩到首格（左上角）。
        Grid.SetRow(overlay, 0);
        Grid.SetColumn(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));
        host.Children.Add(overlay);
    }

    /// <summary>居中模态输入卡片（以遮罩层形式叠加在宿主窗口内）。</summary>
    internal static class FormDialog
    {
        public static Control Build(string title, string subtitle, Control body, string primaryText,
            Func<Task<string?>> submit, Action onClose)
        {
            var error = new TextBlock { FontSize = 12, Foreground = Palette.Red, TextWrapping = TextWrapping.Wrap, IsVisible = false, LineHeight = 19 };

            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Palette.Ink });
            if (!string.IsNullOrEmpty(subtitle))
                head.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = Palette.Txt3, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });

            var cancel = new Button { Content = "取消" };
            cancel.Classes.Add("pill");
            cancel.Click += (_, _) => onClose();
            var primary = new Button { Content = primaryText };
            primary.Classes.Add("primary");
            primary.Click += async (_, _) =>
            {
                primary.IsEnabled = false;
                try
                {
                    var err = await submit();
                    if (err is null) onClose();
                    else { error.Text = err; error.IsVisible = true; }
                }
                catch (Exception ex) { error.Text = ex.Message; error.IsVisible = true; }
                finally { primary.IsEnabled = true; }
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(primary);

            var card = new StackPanel { Spacing = 14 };
            card.Children.Add(head);
            card.Children.Add(body);
            card.Children.Add(error);
            card.Children.Add(buttons);

            var cardBorder = new Border
            {
                Background = Palette.Card,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(22),
                MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 40, Color = Color.Parse("#40000000"), OffsetY = 8 }),
                Child = new ScrollViewer { Content = card, MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };

            // 半透明遮罩：拦截下层交互，点击遮罩空白不关闭（避免误失输入）
            var scrim = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#661E1810")),
                Child = cardBorder
            };
            return scrim;
        }
    }

    /// <summary>同 PageHeader，但副标题是动态控件（例如显示当前项目名）。</summary>
    public static Control PageHeaderWithDynamicSubtitle(string avatar, string title, string staticSubtitle, Control dynamicSubtitle, params Control[] actions)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Palette.Ink });
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrEmpty(staticSubtitle))
            sub.Children.Add(new TextBlock { Text = staticSubtitle, FontSize = 11.5, Foreground = Palette.Txt3 });
        sub.Children.Add(dynamicSubtitle);
        text.Children.Add(sub);

        var ava = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
            Background = Palette.AccentDeep,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = avatar, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Palette.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ava);
        left.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) if (a != null) right.Children.Add(a);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static LabeledControl Row(string label, Control control, double labelWidth = 84)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"), Margin = new Thickness(0, 5, 0, 5) };
        var tb = new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };
        // 控件一律左对齐，避免固定宽度控件在列中居中导致各行不对齐
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(tb);
        grid.Children.Add(control);
        return new LabeledControl(grid) { HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    /// <summary>带分隔线的字段行：左侧标签列（固定宽），右侧控件；对应预览稿 .field。</summary>
    public static Control FormField(string label, Control control, double labelWidth = 96)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*") };
        var tb = new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Txt3, VerticalAlignment = VerticalAlignment.Center };
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(tb);
        grid.Children.Add(control);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(0, 9, 0, 9),
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    /// <summary>
    /// 分段按钮组（枚举选择）：选中项高亮强调色，替代下拉框。
    /// columns &gt; 0 时用等分列网格排布，消除参差换行；否则流式换行。
    /// </summary>
    public static Control Segmented<T>(IReadOnlyList<Pick<T>> options, T? selected, Action<T> onPick, int columns = 0)
    {
        var buttons = new List<Button>();
        Panel wrap;
        if (columns > 0)
            wrap = new UniformGrid { Columns = columns };
        else
            wrap = new WrapPanel();

        var isUniform = wrap is UniformGrid;
        foreach (var opt in options)
        {
            var b = new Button { Content = opt.Label, Tag = opt.Value, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Classes.Add("seg");
            if (isUniform) b.Margin = new Thickness(0, 0, 6, 6);
            if (EqualityComparer<T?>.Default.Equals(selected, opt.Value)) b.Classes.Add("on");
            b.Click += (_, _) =>
            {
                foreach (var bb in buttons) bb.Classes.Remove("on");
                b.Classes.Add("on");
                onPick(opt.Value);
            };
            buttons.Add(b);
            wrap.Children.Add(b);
        }
        return wrap;
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
