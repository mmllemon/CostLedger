using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// 阶段1 「自建对象」：用户在后台新建"对象类型"并管理其记录，无需改代码、免编译。
/// 与 GenericRecordView（系统实体）平行；数据落在 DynamicRecord/DynamicValue（EAV），复用 ControlFactory 渲染。
/// 例：设备台账 / 资质证书 / 车辆 —— 建类型(填字段) → 录记录 → 自动列表+审计。
/// </summary>
public partial class DynamicEntityView : UserControl
{
    private readonly MetadataService _metadata = null!;
    private readonly DynamicEntityService _dyn = null!;

    private List<EntityDef> _entities = new();
    private EntityDef? _selectedEntity;
    private List<FieldDef> _fields = new();
    private List<DynamicRecord> _records = new();
    private int _currentId;

    private ComboBox? _entityCombo;
    private ListBox? _recordList;
    private StackPanel? _formHost;
    private TextBox? _codeBox;
    private TextBox? _nameBox;
    private readonly List<(FieldDef Def, Control Input)> _fieldRows = new();

    public DynamicEntityView(AppDbContext db, MetadataService metadata)
    {
        _metadata = metadata;
        _dyn = new DynamicEntityService(db);
        BuildUi();
        _ = InitAsync();
    }

    private SolidColorBrush Brush(string key) => Palette.Find(key);

    private void BuildUi()
    {
        _entityCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "选择对象类型", Margin = new Thickness(0, 0, 8, 0) };
        _entityCombo.SelectionChanged += OnEntityChanged;
        var newEntityBtn = new Button { Content = "新建对象类型", Classes = { "primary" } };
        newEntityBtn.Click += OnNewEntity;

        var leftTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        leftTop.Children.Add(_entityCombo);
        leftTop.Children.Add(newEntityBtn);

        _recordList = new ListBox { Margin = new Thickness(0, 0, 0, 8), MaxHeight = 380 };
        _recordList.SelectionChanged += OnRecordSelected;
        var newRecBtn = new Button { Content = "＋ 新建记录", HorizontalAlignment = HorizontalAlignment.Stretch };
        newRecBtn.Click += OnNewRecord;

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0), Spacing = 4 };
        left.Children.Add(leftTop);
        left.Children.Add(_recordList);
        left.Children.Add(newRecBtn);

        _codeBox = new TextBox { Watermark = "编号", Padding = new Thickness(10, 8) };
        _nameBox = new TextBox { Watermark = "名称", Padding = new Thickness(10, 8) };
        _formHost = new StackPanel { Spacing = 2 };
        var saveBtn = new Button { Content = "保存", Classes = { "primary" }, Margin = new Thickness(0, 8, 8, 0) };
        saveBtn.Click += OnSave;
        var delBtn = new Button { Content = "删除", Classes = { "danger" }, Margin = new Thickness(0, 8, 0, 0) };
        delBtn.Click += OnDelete;
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(delBtn);

        var right = new ScrollViewer { Content = new StackPanel { Spacing = 2, Children = { _formHost, btnRow } }, Padding = new Thickness(4) };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,1*"), Margin = new Thickness(14) };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Content = grid;
    }

    private async Task InitAsync()
    {
        _entities = await _dyn.ListUserEntitiesAsync();
        _entityCombo!.ItemsSource = _entities;
        _entityCombo.DisplayMemberBinding = new Binding("Name");
        _entityCombo.SelectedValueBinding = new Binding("Code");
        if (_entities.Count > 0) _entityCombo.SelectedIndex = 0;
        else RenderEmpty();
    }

    private void RenderEmpty()
    {
        _formHost!.Children.Clear();
        _formHost.Children.Add(new TextBlock
        {
            Text = "尚未自建对象类型。点击左上「新建对象类型」，填写名称与字段即可拥有专属台账（如：设备台账 / 资质证书 / 车辆）。",
            Foreground = Brush("Txt3Brush"), FontSize = 13, Margin = new Thickness(4, 8), TextWrapping = TextWrapping.Wrap
        });
    }

    private async void OnEntityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_entityCombo!.SelectedValue is not string code) { RenderEmpty(); return; }
        _selectedEntity = _entities.FirstOrDefault(x => x.Code == code);
        await LoadEntityAsync(code);
    }

    private async Task LoadEntityAsync(string code)
    {
        _fields = await _dyn.ListFieldsAsync(code);
        _records = await _dyn.ListRecordsAsync(code);
        _recordList!.ItemsSource = _records;
        _recordList.DisplayMemberBinding = new Binding("Name");
        ClearForm();
        if (_records.Count > 0) _recordList.SelectedIndex = 0;
        else if (_fields.Count == 0)
        {
            _formHost!.Children.Clear();
            _formHost.Children.Add(new TextBlock
            {
                Text = "该对象类型还没有字段。请到「系统设置 → 字段管理」选此对象添加字段，或用「新建对象类型」时在字段框内填写。",
                Foreground = Brush("Txt3Brush"), FontSize = 12.5, Margin = new Thickness(4, 8), TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async void OnRecordSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_recordList!.SelectedItem is not DynamicRecord rec) { ClearForm(); return; }
        _currentId = rec.Id;
        _codeBox!.Text = rec.Code;
        _nameBox!.Text = rec.Name;
        var (_, values) = await _dyn.GetRecordAsync(rec.Id);
        foreach (var (def, input) in _fieldRows)
            ControlFactory.SetInput(input, def, values.TryGetValue(def.Id, out var v) ? v : null);
    }

    private void ClearForm()
    {
        _currentId = 0;
        _codeBox!.Text = "";
        _nameBox!.Text = "";
        _formHost!.Children.Clear();
        _fieldRows.Clear();

        _formHost.Children.Add(FieldRow("编号 *", _codeBox));
        _formHost.Children.Add(FieldRow("名称 *", _nameBox));
        foreach (var f in _fields)
        {
            var input = BuildFieldInput(f);
            _fieldRows.Add((f, input));
            _formHost.Children.Add(FieldRow(f.IsRequired ? $"{f.Name} *" : f.Name, input));
        }
    }

    private Control FieldRow(string label, Control input)
    {
        var wrap = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        wrap.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
        wrap.Children.Add(input);
        return wrap;
    }

    private Control BuildFieldInput(FieldDef f)
    {
        if (f.Type == FieldType.Lookup && !string.IsNullOrEmpty(f.LookupDefCode))
        {
            var items = _metadata.ListLookupItemsAsync(f.LookupDefCode!).GetAwaiter().GetResult();
            return ControlFactory.BuildInput(f.Type, null, items);
        }
        return ControlFactory.BuildInput(f.Type, null);
    }

    private void OnNewRecord(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntity is null) { Warn("请先选择或新建一个对象类型。"); return; }
        _recordList!.SelectedItem = null;
        ClearForm();
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntity is null) { Warn("请先选择或新建一个对象类型。"); return; }
        var code = _codeBox!.Text?.Trim() ?? "";
        var name = _nameBox!.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) { Warn("编号与名称为必填。"); return; }

        var values = new Dictionary<int, string>();
        foreach (var (def, input) in _fieldRows)
            values[def.Id] = ControlFactory.ReadInput(input, def.Type);

        _currentId = await _dyn.SaveRecordAsync(_selectedEntity.Code, _currentId, code, name, values);
        await LoadEntityAsync(_selectedEntity.Code);
        _recordList!.SelectedValue = _currentId;
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_currentId == 0) return;
        if (!Confirm("确认删除该记录？此操作不可撤销。")) return;
        await _dyn.DeleteRecordAsync(_currentId);
        _currentId = 0;
        await LoadEntityAsync(_selectedEntity!.Code);
    }

    private async void OnNewEntity(object? sender, RoutedEventArgs e)
        => await OpenNewEntityDialogAsync();

    private async Task OpenNewEntityDialogAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var nameBox = new TextBox { Watermark = "如：设备台账", Width = 320 };
        var codeBox = new TextBox { Watermark = "如：EQUIP", Width = 320 };
        var fieldsBox = new TextBox
        {
            Watermark = "每行一个字段，如：\n规格型号\n原值:金额\n购置日期:日期\n是否停用:是否",
            Width = 320, Height = 140, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap
        };

        var form = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        form.Children.Add(Labeled("对象名称 *", nameBox));
        form.Children.Add(Labeled("对象编码 *（英文/数字）", codeBox));
        form.Children.Add(Labeled("字段（每行一个，可加 :类型）", fieldsBox));

        var ok = new Button { Content = "创建", Classes = { "primary" } };
        var cancel = new Button { Content = "取消" };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        form.Children.Add(btnRow);

        var dlg = new Window { Title = "新建对象类型", Width = 380, Height = 360, Content = form, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        bool done = false;
        ok.Click += async (_, _) =>
        {
            var name = nameBox.Text?.Trim();
            var code = codeBox.Text?.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code)) { Warn("名称与编码必填。"); return; }
            if (_entities.Any(x => x.Code == code)) { Warn("编码已存在。"); return; }
            var fields = ParseFields(fieldsBox.Text ?? "");
            await _dyn.CreateUserEntityAsync(name, code, fields);
            done = true;
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();

        if (owner is not null) await dlg.ShowDialog(owner);
        else dlg.Show();

        if (done)
        {
            _entities = await _dyn.ListUserEntitiesAsync();
            _entityCombo!.ItemsSource = _entities;
            _entityCombo.SelectedValue = codeBox.Text?.Trim();
        }
    }

    private static List<(string Name, FieldType Type)> ParseFields(string text)
    {
        var result = new List<(string, FieldType)>();
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            var parts = t.Split(':');
            var fname = parts[0].Trim();
            if (string.IsNullOrEmpty(fname)) continue;
            var type = FieldType.Text;
            if (parts.Length > 1)
                type = parts[1].Trim() switch
                {
                    "数字" => FieldType.Number,
                    "金额" => FieldType.Money,
                    "日期" => FieldType.Date,
                    "是否" or "布尔" => FieldType.Boolean,
                    "下拉" => FieldType.Lookup,
                    _ => FieldType.Text
                };
            result.Add((fname, type));
        }
        return result;
    }

    private static StackPanel Labeled(string label, Control input)
    {
        var wrap = new StackPanel { Spacing = 4 };
        wrap.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
        wrap.Children.Add(input);
        return wrap;
    }

    private void Warn(string msg)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var w = new Window { Title = "提示", Width = 320, Height = 150, Content = new TextBlock { Text = msg, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "知道了", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        ok.Click += (_, _) => w.Close();
        ((StackPanel)w.Content!).Children.Add(ok);
        if (owner is not null) w.ShowDialog(owner); else w.Show();
    }

    private bool Confirm(string msg)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        bool result = false;
        var w = new Window { Title = "确认", Width = 340, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap });
        var ok = new Button { Content = "确认删除", Classes = { "danger" }, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { result = true; w.Close(); };
        sp.Children.Add(ok);
        w.Content = sp;
        if (owner is not null) w.ShowDialog(owner); else w.Show();
        return result;
    }
}
