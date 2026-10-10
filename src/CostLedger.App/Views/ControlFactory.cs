using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using CostLedger.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CostLedger.App.Views;

/// <summary>
/// 字段控件工厂：把 FieldType 映射为界面控件、以及把控件读回字符串的单一来源。
/// 供 DynamicFieldsPanel（扩展字段）与 GenericRecordView（核心字段）复用，避免重复造轮子。
/// </summary>
internal static class ControlFactory
{
    /// <summary>按字段类型生成输入控件。Lookup 需传入对应字典项列表。</summary>
    public static Control BuildInput(FieldType type, string? current, IReadOnlyList<LookupItem>? lookupItems = null)
    {
        switch (type)
        {
            case FieldType.Boolean:
                return new CheckBox
                {
                    IsChecked = string.Equals(current, "True", StringComparison.OrdinalIgnoreCase),
                    VerticalAlignment = VerticalAlignment.Center
                };

            case FieldType.Number:
            case FieldType.Money:
                var num = new NumericUpDown
                {
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    Padding = new Thickness(10, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                num.FormatString = type == FieldType.Money ? "N2" : "0.####";
                if (decimal.TryParse(current, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
                    num.Value = dv;
                return num;

            case FieldType.Date:
                var dp = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
                if (DateOnly.TryParse(current, out var d))
                    dp.SelectedDate = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue));
                return dp;

            case FieldType.Lookup:
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "请选择" };
                if (lookupItems is { Count: > 0 })
                {
                    combo.ItemsSource = lookupItems;
                    combo.DisplayMemberBinding = new Binding("Name");
                    combo.SelectedValueBinding = new Binding("Code");
                    combo.SelectedValue = current;
                }
                return combo;

            default: // Text
                return new TextBox
                {
                    Text = current ?? "",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 8)
                };
        }
    }

    /// <summary>把控件当前值读回为字符串（与 BuildInput 对称）。</summary>
    public static string ReadInput(Control input, FieldType type) => type switch
    {
        FieldType.Boolean => input is CheckBox cb ? (cb.IsChecked == true ? "True" : "False") : "",
        FieldType.Number or FieldType.Money => input is NumericUpDown nu && nu.Value.HasValue
            ? nu.Value.Value.ToString(CultureInfo.InvariantCulture) : "",
        FieldType.Date => input is DatePicker dp && dp.SelectedDate.HasValue
            ? DateOnly.FromDateTime(dp.SelectedDate.Value.DateTime).ToString("yyyy-MM-dd") : "",
        FieldType.Lookup => input is ComboBox cmb ? cmb.SelectedValue?.ToString() ?? "" : "",
        _ => input is TextBox t ? t.Text ?? "" : ""
    };

    /// <summary>把已存值写回控件（编辑回填，与 BuildInput 的初始值逻辑对称）。</summary>
    public static void SetInput(Control input, FieldDef def, string? value)
    {
        switch (def.Type)
        {
            case FieldType.Boolean:
                if (input is CheckBox cb) cb.IsChecked = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                break;
            case FieldType.Number or FieldType.Money:
                if (input is NumericUpDown nu && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv)) nu.Value = dv;
                break;
            case FieldType.Date:
                if (input is DatePicker dp && DateOnly.TryParse(value, out var d)) dp.SelectedDate = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue));
                break;
            case FieldType.Lookup:
                if (input is ComboBox cmb) cmb.SelectedValue = value;
                break;
            default:
                if (input is TextBox t) t.Text = value ?? "";
                break;
        }
    }
}
