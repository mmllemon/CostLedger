using Avalonia.Data.Converters;
using Avalonia.Media;
using CostLedger.Domain;
using System;
using System.Globalization;

namespace CostLedger.App;

/// <summary>看板 KPI 阈值着色：告警红 / 预警陶土橙 / 正常墨色</summary>
public class KpiSeverityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Normal = new(Color.Parse("#211E1A"));
    private static readonly SolidColorBrush Warn = new(Color.Parse("#A24E30"));
    private static readonly SolidColorBrush Danger = new(Color.Parse("#BE4A3C"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is KpiSeverity s
            ? s switch { KpiSeverity.Danger => Danger, KpiSeverity.Warn => Warn, _ => Normal }
            : Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
