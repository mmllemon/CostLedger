using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 通用动态字段面板：读取某实体的「扩展字段」(FieldDef.IsCore=false) 自动渲染表单，
/// 并在保存时把值写入 CustomFieldValue（EAV）。
/// 后台"字段管理"新增字段后，本面板无需改代码即自动多出对应控件——这就是 M2-A "全动态"体验。
/// </summary>
public partial class DynamicFieldsPanel : UserControl
{
    private readonly MetadataService _metadata = null!;
    private string _entityCode = "";
    private int _ownerId;
    private readonly List<(FieldDef Def, Control Input)> _rows = new();

    public DynamicFieldsPanel()
    {
        InitializeComponent();
    }

    public DynamicFieldsPanel(MetadataService metadata) : this()
    {
        _metadata = metadata;
    }

    private SolidColorBrush Brush(string key) => (SolidColorBrush)this.FindResource(key)!;

    public async Task LoadAsync(string entityCode, int ownerId)
    {
        _entityCode = entityCode;
        _ownerId = ownerId;
        Host.Children.Clear();
        _rows.Clear();

        var fields = (await _metadata.ListFieldDefsAsync(entityCode)).Where(f => !f.IsCore).OrderBy(f => f.DisplayOrder).ToList();
        var values = ownerId > 0 ? await _metadata.GetCustomValuesAsync(entityCode, ownerId) : new Dictionary<int, string>();

        if (fields.Count == 0)
        {
            Host.Children.Add(new TextBlock
            {
                Text = "（暂无扩展字段。可在「系统设置 → 字段管理」为本模块新增字段。）",
                Foreground = Brush("Txt3Brush"),
                FontSize = 12,
                Margin = new Thickness(2, 4, 0, 4)
            });
            return;
        }

        foreach (var f in fields)
        {
            values.TryGetValue(f.Id, out var current);
            var input = await BuildControlAsync(f, current);
            _rows.Add((f, input));

            var label = new TextBlock
            {
                Text = f.IsRequired ? $"{f.Name} *" : f.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var wrap = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            wrap.Children.Add(label);
            wrap.Children.Add(input);
            Host.Children.Add(wrap);
        }
    }

    public async Task SaveAsync()
    {
        if (_ownerId <= 0) return; // 新记录尚未落库，无法挂扩展字段值
        var values = new Dictionary<int, string>();
        foreach (var (def, input) in _rows)
        {
            values[def.Id] = ReadControl(def, input);
        }
        await _metadata.SaveCustomValuesAsync(_entityCode, _ownerId, values);
    }

    public void SetOwnerId(int ownerId) => _ownerId = ownerId;

    private async Task<Control> BuildControlAsync(FieldDef f, string? current)
    {
        List<LookupItem>? items = null;
        if (f.Type == FieldType.Lookup && !string.IsNullOrEmpty(f.LookupDefCode))
            items = await _metadata.ListLookupItemsAsync(f.LookupDefCode!);
        return ControlFactory.BuildInput(f.Type, current, items);
    }

    private string ReadControl(FieldDef f, Control input) => ControlFactory.ReadInput(input, f.Type);
}
