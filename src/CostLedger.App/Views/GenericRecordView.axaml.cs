using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>核心字段渲染类型（代码侧描述，不进元数据表）</summary>
public enum CoreFieldKind { Text, Number, Money, Date, Boolean, Lookup, Enum }

/// <summary>核心字段描述：标签 + C# 属性名 + 类型；Lookup 需字典代码，Enum 需枚举类型</summary>
public record CoreFieldDescriptor(
    string Label,
    string Property,
    CoreFieldKind Kind,
    bool IsRequired = false,
    string? LookupDefCode = null,
    Type? EnumType = null,
    int DisplayOrder = 0);

/// <summary>
/// M2-B 通用记录编辑器：一份代码驱动任意"扁平实体"的列表 + 详情编辑。
/// - 核心字段：依据 CoreFieldDescriptor 经反射渲染强类型属性（参与业务/成本）；
/// - 扩展字段：复用 DynamicFieldsPanel，后台"字段管理"加字段即生效（不参与成本归集）。
/// 已用于「项目」「成本科目」；后续计量/付款等复杂实体亦可按此模式接入。
/// </summary>
public partial class GenericRecordView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly MetadataService _metadata = null!;
    private readonly string _entityCode = null!;
    private readonly CoreFieldDescriptor[] _core = null!;
    private readonly Func<object> _factory = null!;
    private readonly Func<object, string> _display = null!;
    private readonly Func<AppDbContext, IEnumerable<object>> _queryAll = null!;

    private readonly Dictionary<string, List<LookupItem>> _lookups = new();
    private readonly List<(CoreFieldDescriptor Desc, Control Input)> _coreRows = new();
    private DynamicFieldsPanel? _ext;
    private int _currentId;
    private object? _currentModel;
    private readonly string? _parentField;
    private readonly string? _pathField;
    private List<object> _allModels = new();
    private ComboBox? _parentInput;

    public GenericRecordView()
    {
        InitializeComponent();
    }

    public GenericRecordView(AppDbContext db, MetadataService metadata, string entityCode,
        Type entityType, CoreFieldDescriptor[] core, Func<object> factory, Func<object, string> display,
        Func<AppDbContext, IEnumerable<object>> queryAll, string? parentField = null, string? pathField = null)
        : this()
    {
        _db = db; _metadata = metadata; _entityCode = entityCode;
        _core = core; _factory = factory; _display = display; _queryAll = queryAll;
        _parentField = parentField; _pathField = pathField;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _lookups.Clear();
        foreach (var code in _core.Where(c => c.Kind == CoreFieldKind.Lookup && !string.IsNullOrEmpty(c.LookupDefCode))
                     .Select(c => c.LookupDefCode!).Distinct())
            _lookups[code] = await _metadata.ListLookupItemsAsync(code);

        _ext = new DynamicFieldsPanel(_metadata);
        ExtHost.Children.Add(_ext);

        await RefreshListAsync();
    }

    // ---------- 列表 ----------
    private record DisplayRow(int Id, string Text, object Model);

    private async Task RefreshListAsync(int selectId = 0)
    {
        var models = await Task.Run(() => _queryAll(_db).ToList());
        _allModels = models;
        var rows = models.Select(m => new DisplayRow(
            (int)PropertyAccessor.Get(m, "Id")!, _display(m), m)).ToList();
        RecordList.ItemsSource = rows;
        RecordList.DisplayMemberBinding = new Binding("Text");
        if (selectId > 0)
            RecordList.SelectedItem = rows.FirstOrDefault(r => r.Id == selectId);
        else if (rows.Count > 0)
            RecordList.SelectedIndex = 0;
    }

    // ---------- 详情 ----------
    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecordList.SelectedItem is not DisplayRow row) return;
        _currentId = row.Id;
        _currentModel = row.Model;
        RenderCore(row.Model);
        if (_ext != null) await _ext.LoadAsync(_entityCode, _currentId);
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        _currentId = 0;
        _currentModel = null;
        RecordList.SelectedItem = null;
        RenderCore(_factory());
        _ = (_ext?.LoadAsync(_entityCode, 0) ?? Task.CompletedTask);
    }

    private void RenderCore(object model)
    {
        CoreHost.Children.Clear();
        _coreRows.Clear();
        foreach (var d in _core.OrderBy(x => x.DisplayOrder))
        {
            object? cur = PropertyAccessor.Get(model, d.Property);
            string current = d.Kind switch
            {
                CoreFieldKind.Boolean => cur is bool b && b ? "True" : "False",
                CoreFieldKind.Number or CoreFieldKind.Money => cur is decimal m ? m.ToString(CultureInfo.InvariantCulture) : "",
                CoreFieldKind.Date => cur is DateOnly dt ? dt.ToString("yyyy-MM-dd") : "",
                CoreFieldKind.Enum => cur?.ToString() ?? "",
                _ => cur?.ToString() ?? ""
            };
            var input = d.Kind == CoreFieldKind.Enum
                ? BuildEnumCombo(d, current)
                : ControlFactory.BuildInput(Map(d.Kind), current, d.Kind == CoreFieldKind.Lookup ? LookupItemsFor(d) : null);
            _coreRows.Add((d, input));

            var label = new TextBlock
            {
                Text = d.IsRequired ? $"{d.Label} *" : d.Label,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var wrap = new StackPanel { Spacing = 4 };
            wrap.Children.Add(label);
            wrap.Children.Add(input);
            CoreHost.Children.Add(wrap);
        }
        if (_parentField != null)
            RenderParent(model);
    }

    private void RenderParent(object model)
    {
        var curId = (int)PropertyAccessor.Get(model, "Id")!;
        var candidates = _allModels
            .Where(m => (int)PropertyAccessor.Get(m, "Id")! != curId && !IsDescendant(m, curId))
            .ToList();
        var cb = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "（顶级节点，无父）",
            DisplayMemberBinding = new Binding(_pathField ?? "Name"),
            SelectedValueBinding = new Binding("Id")
        };
        cb.ItemsSource = candidates;
        var curParent = PropertyAccessor.Get(model, _parentField!);
        cb.SelectedValue = curParent is int iv ? iv : null;
        _parentInput = cb;

        var label = new TextBlock { Text = "父节点", FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) };
        var wrap = new StackPanel { Spacing = 4 };
        wrap.Children.Add(label);
        wrap.Children.Add(cb);
        CoreHost.Children.Add(wrap);
    }

    private bool IsDescendant(object m, int ancestorId)
    {
        var p = PropertyAccessor.Get(m, _parentField!);
        if (p is null) return false;
        var pid = (int)p;
        if (pid == ancestorId) return true;
        var parent = _allModels.FirstOrDefault(x => (int)PropertyAccessor.Get(x, "Id")! == pid);
        return parent != null && IsDescendant(parent, ancestorId);
    }

    private Control BuildEnumCombo(CoreFieldDescriptor d, string current)
    {
        var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "请选择" };
        cb.ItemsSource = Enum.GetNames(d.EnumType!);
        cb.SelectedItem = string.IsNullOrEmpty(current) ? null : current;
        return cb;
    }

    private List<LookupItem>? LookupItemsFor(CoreFieldDescriptor d)
        => d.LookupDefCode != null && _lookups.TryGetValue(d.LookupDefCode, out var list) ? list : null;

    private static FieldType Map(CoreFieldKind k) => k switch
    {
        CoreFieldKind.Number => FieldType.Number,
        CoreFieldKind.Money => FieldType.Money,
        CoreFieldKind.Date => FieldType.Date,
        CoreFieldKind.Boolean => FieldType.Boolean,
        CoreFieldKind.Lookup => FieldType.Lookup,
        _ => FieldType.Text
    };

    // ---------- 保存 / 删除 ----------
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var model = _currentId == 0 ? _factory() : _currentModel!;
        ApplyCore(model);
        if (_currentId == 0) _db.Add(model);
        await _db.SaveChangesAsync();

        if (_currentId == 0)
        {
            _currentId = (int)PropertyAccessor.Get(model, "Id")!;
            _ext?.SetOwnerId(_currentId);
        }
        if (_ext != null) await _ext.SaveAsync();

        if (_pathField != null) await RebuildPaths();

        await RefreshListAsync(selectId: _currentId);
    }

    private async Task RebuildPaths()
    {
        var all = _queryAll(_db).Cast<object>().ToList();
        var childrenMap = all.ToLookup(m => (int?)PropertyAccessor.Get(m, _parentField!));
        void Walk(int? pid, string prefix)
        {
            foreach (var k in childrenMap[pid])
            {
                var name = (string)PropertyAccessor.Get(k, "Name")!;
                var path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
                PropertyAccessor.Set(k, _pathField!, path);
                Walk((int?)PropertyAccessor.Get(k, "Id"), path);
            }
        }
        Walk(null, "");
        await _db.SaveChangesAsync();
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_currentId == 0 || _currentModel is null) return;
        if (_parentField != null)
        {
            var hasChild = _queryAll(_db).Any(m => (int?)PropertyAccessor.Get(m, _parentField!) == _currentId);
            if (hasChild) { Warn("该节点下还有子节点，请先删除其子节点。"); return; }
        }
        var orphans = await _db.CustomFieldValues
            .Where(x => x.EntityDefCode == _entityCode && x.OwnerId == _currentId).ToListAsync();
        _db.CustomFieldValues.RemoveRange(orphans);
        _db.Remove(_currentModel);
        await _db.SaveChangesAsync();
        _currentId = 0;
        _currentModel = null;
        await RefreshListAsync();
    }

    private void Warn(string msg)
    {
        var w = new Window
        {
            Title = "提示",
            Width = 340,
            Height = 150,
            Content = new TextBlock { Text = msg, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }
        };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) _ = w.ShowDialog(owner);
        else w.Show();
    }

    private void ApplyCore(object model)
    {
        foreach (var (d, input) in _coreRows)
        {
            if (d.Kind == CoreFieldKind.Enum)
            {
                var name = (input as ComboBox)?.SelectedItem as string;
                if (!string.IsNullOrEmpty(name) && d.EnumType != null)
                    PropertyAccessor.Set(model, d.Property, Enum.Parse(d.EnumType, name));
                continue;
            }
            var str = ControlFactory.ReadInput(input, Map(d.Kind));
            switch (d.Kind)
            {
                case CoreFieldKind.Boolean:
                    PropertyAccessor.Set(model, d.Property, string.Equals(str, "True", StringComparison.OrdinalIgnoreCase));
                    break;
                case CoreFieldKind.Number or CoreFieldKind.Money:
                    PropertyAccessor.Set(model, d.Property, decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) ? dec : 0m);
                    break;
                case CoreFieldKind.Date:
                    PropertyAccessor.Set(model, d.Property, DateOnly.TryParse(str, out var dt) ? dt : default(DateOnly));
                    break;
                default: // Text / Lookup
                    PropertyAccessor.Set(model, d.Property, str);
                    break;
            }
        }
        if (_parentField != null && _parentInput != null)
        {
            var pid = _parentInput.SelectedValue as int?;
            PropertyAccessor.Set(model, _parentField!, pid);
        }
    }
}
