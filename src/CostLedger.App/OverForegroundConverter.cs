using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace CostLedger.App;

/// <summary>超支文字着色：IsOver=true 用异常红，否则默认墨色</summary>
public class OverForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Over = new(Color.Parse("#BE4A3C"));
    private static readonly SolidColorBrush Normal = new(Color.Parse("#211E1A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Over : Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
