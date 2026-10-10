using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

public partial class SystemSettingsView : UserControl
{
    private readonly MetadataService _metadata = null!;

    private List<LookupDef> _lookupDefs = new();
    private LookupDef? _selectedLookupDef;
    private List<LookupItem> _lookupItems = new();
    private readonly List<(LookupItem Item, TextBox Code, TextBox Name, NumericUpDown Order, CheckBox Enabled, TextBox Color)> _itemRows = new();

    private readonly SystemParamService? _params;
    private readonly List<(SystemParam Param, TextBox Key, TextBox Value, TextBox Desc)> _paramRows = new();

    private List<EntityDef> _entities = new();
    private EntityDef? _selectedEntity;
    private List<FieldDef> _fields = new();
    private readonly List<(FieldDef Def, TextBox Name, ComboBox Type, CheckBox Required, ComboBox Lookup, CheckBox Core, NumericUpDown Order)> _fieldRows = new();

    private static readonly List<(int Value, string Name)> FieldTypeOptions = new()
    {
        ((int)FieldType.Text, "文本"),
        ((int)FieldType.Number, "数字"),
        ((int)FieldType.Money, "金额"),
        ((int)FieldType.Date, "日期"),
        ((int)FieldType.Boolean, "是/否"),
        ((int)FieldType.Lookup, "下拉(字典)"),
    };

    public SystemSettingsView()
    {
        InitializeComponent();
    }

    public SystemSettingsView(MetadataService metadata) : this()
    {
        _metadata = metadata;
        InitBackupInfo();
        _ = InitAsync();
    }

    public SystemSettingsView(MetadataService metadata, AppDbContext db) : this(metadata)
    {
        _audit = new AuditService(db);
        _params = new SystemParamService(db);
        _pdb = db;
        _ = LoadAuditAsync();
        _ = LoadParamsAsync();
    }

    private AuditService? _audit;

    private async Task LoadAuditAsync()
    {
        if (_audit is null) return;
        var logs = await _audit.ListAsync(AuditFilter.Text);
        RenderAudit(logs);
        AuditStatus.Text = $"共 {logs.Count} 条（最近 500 条）";
    }

    private void RenderAudit(System.Collections.Generic.List<AuditLog> logs)
    {
        AuditHost.Children.Clear();
        AuditHost.ColumnDefinitions.Clear();
        AuditHost.RowDefinitions.Clear();

        var headers = new[] { "时间", "实体", "ID", "动作", "操作人" };
        for (int c = 0; c < headers.Length; c++)
            AuditHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < logs.Count + 1; i++)
            AuditHost.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < headers.Length; c++)
            AuditHost.Children.Add(AuditCell(headers[c], 0, c, head: true));

        for (int i = 0; i < logs.Count; i++)
        {
            var a = logs[i];
            var cells = new[]
            {
                a.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                a.EntityName,
                a.EntityId.ToString(),
                a.Action switch { AuditAction.Create => "新增", AuditAction.Update => "修改", _ => "删除" },
                a.By ?? ""
            };
            for (int c = 0; c < headers.Length; c++)
                AuditHost.Children.Add(AuditCell(cells[c], i + 1, c, head: false));
        }
    }

    private TextBlock AuditCell(string text, int row, int col, bool head)
    {
        var tb = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 0, 22, 6) };
        if (head)
        {
            tb.FontWeight = FontWeight.Bold;
            tb.Foreground = (SolidColorBrush)this.FindResource("Txt3Brush")!;
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    private async void OnAuditQuery(object? sender, RoutedEventArgs e) => await LoadAuditAsync();

    private void InitBackupInfo()
    {
        DbPathText.Text = DbBootstrap.CurrentDbPath ?? "(未知)";
        DbVersionText.Text = $"结构版本：v{DbBootstrap.SchemaVersion}";
    }

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        var path = DbBootstrap.CurrentDbPath;
        if (path is null || !File.Exists(path)) { BackupStatus.Text = "未找到账套文件"; return; }
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"costledger_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            FileTypeChoices = new[] { new FilePickerFileType("SQLite 账套") { Patterns = new[] { "*.db" } } }
        });
        if (file is null) return;
        var target = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(target)) { BackupStatus.Text = "该位置不支持写入"; return; }

        try
        {
            DbBootstrap.BackupTo(path, target);
            BackupStatus.Text = "备份完成：" + Path.GetFileName(target);
        }
        catch (Exception ex)
        {
            BackupStatus.Text = "备份失败：" + ex.Message;
        }
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var path = DbBootstrap.CurrentDbPath;
        if (path is null) return;
        var dir = Path.GetDirectoryName(path)!;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private SolidColorBrush Brush(string key) => (SolidColorBrush)this.FindResource(key)!;

    private async Task InitAsync()
    {
        _lookupDefs = await _metadata.ListLookupDefsAsync();
        LookupDefList.ItemsSource = _lookupDefs;
        LookupDefList.DisplayMemberBinding = new Binding("Name");
        if (_lookupDefs.Count > 0) LookupDefList.SelectedIndex = 0;

        _entities = await _metadata.ListEntityDefsAsync();
        EntityCombo.ItemsSource = _entities;
        EntityCombo.DisplayMemberBinding = new Binding("Name");
        EntityCombo.SelectedValueBinding = new Binding("Code");
        if (_entities.Count > 0) EntityCombo.SelectedIndex = 0;
    }

    // ---------------- 字典管理 ----------------
    private void OnLookupDefSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LookupDefList.SelectedItem is not LookupDef def) return;
        _selectedLookupDef = def;
        LookupDefTitle.Text = def.Name;
        _ = LoadLookupItemsAsync(def.Code);
    }

    private async Task LoadLookupItemsAsync(string code)
    {
        _lookupItems = await _metadata.ListLookupItemsAsync(code);
        BuildLookupItemRows();
    }

    private void BuildLookupItemRows()
    {
        LookupItemPanel.Children.Clear();
        _itemRows.Clear();
        foreach (var it in _lookupItems)
        {
            var code = new TextBox { Text = it.Code, Width = 120, Padding = new Thickness(8) };
            var name = new TextBox { Text = it.Name, Width = 200, Padding = new Thickness(8) };
            var order = new NumericUpDown { Value = it.SortOrder, Minimum = 0, Maximum = 9999, Width = 90 };
            var enabled = new CheckBox { IsChecked = it.IsEnabled, Content = "启用", VerticalAlignment = VerticalAlignment.Center };
            var color = new TextBox { Text = it.Color ?? "", Width = 92, Padding = new Thickness(8), Watermark = "#CC785C" };
            var del = new Button { Content = "删除" }; del.Classes.Add("danger");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = "代码", VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)Brush("Txt3Brush") });
            row.Children.Add(code);
            row.Children.Add(new TextBlock { Text = "名称", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(name);
            row.Children.Add(new TextBlock { Text = "序", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(order);
            row.Children.Add(enabled);
            row.Children.Add(new TextBlock { Text = "色", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(color);
            row.Children.Add(del);
            del.Click += (_, _) => { _lookupItems.Remove(it); BuildLookupItemRows(); };
            _itemRows.Add((it, code, name, order, enabled, color));
            LookupItemPanel.Children.Add(row);
        }
    }

    private void OnAddLookupItem(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedLookupDef is null) return;
        _lookupItems.Add(new LookupItem { LookupDefId = _selectedLookupDef.Id, SortOrder = _lookupItems.Count + 1 });
        BuildLookupItemRows();
    }

    private async void OnSaveLookupItems(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedLookupDef is null) return;
        var def = _selectedLookupDef;
        foreach (var (item, code, name, order, enabled, color) in _itemRows)
        {
            item.Code = code.Text?.Trim() ?? "";
            item.Name = name.Text?.Trim() ?? "";
            item.SortOrder = (int)(order.Value ?? 0);
            item.IsEnabled = enabled.IsChecked == true;
            var c = color.Text?.Trim();
            item.Color = string.IsNullOrEmpty(c) ? null : c;
            item.LookupDefId = def.Id;
            if (!string.IsNullOrEmpty(item.Code) && !string.IsNullOrEmpty(item.Name))
                await _metadata.SaveLookupItemAsync(item);
        }
        await LoadLookupItemsAsync(def.Code);
    }

    private async void OnAddLookupDef(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var def = new LookupDef { Code = "NEW_" + Guid.NewGuid().ToString("N")[..6], Name = "新字典" };
        await _metadata.SaveLookupDefAsync(def);
        _lookupDefs = await _metadata.ListLookupDefsAsync();
        LookupDefList.ItemsSource = _lookupDefs;
        LookupDefList.SelectedIndex = _lookupDefs.FindIndex(x => x.Id == def.Id);
    }

    // ---------------- 字段管理 ----------------
    private void OnEntitySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (EntityCombo.SelectedValue is not string code) return;
        _selectedEntity = _entities.FirstOrDefault(x => x.Code == code);
        _ = LoadFieldsAsync(code);
    }

    private async Task LoadFieldsAsync(string code)
    {
        _fields = await _metadata.ListFieldDefsAsync(code);
        BuildFieldRows();
    }

    private void BuildFieldRows()
    {
        FieldPanel.Children.Clear();
        _fieldRows.Clear();
        foreach (var f in _fields)
        {
            var name = new TextBox { Text = f.Name, Width = 160, Padding = new Thickness(8) };
            var type = new ComboBox { ItemsSource = FieldTypeOptions, DisplayMemberBinding = new Binding("Name"), SelectedValueBinding = new Binding("Value"), Width = 110 };
            type.SelectedValue = (int)f.Type;
            var req = new CheckBox { IsChecked = f.IsRequired, Content = "必填", VerticalAlignment = VerticalAlignment.Center };
            var lookup = new ComboBox { ItemsSource = _lookupDefs, DisplayMemberBinding = new Binding("Name"), SelectedValueBinding = new Binding("Code"), Width = 140, PlaceholderText = "字典" };
            lookup.SelectedValue = f.LookupDefCode;
            var core = new CheckBox { IsChecked = f.IsCore, Content = "核心", VerticalAlignment = VerticalAlignment.Center };
            var order = new NumericUpDown { Value = f.DisplayOrder, Minimum = 0, Maximum = 9999, Width = 80 };
            var del = new Button { Content = "删除" }; del.Classes.Add("danger");

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock { Text = "字段名", VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)Brush("Txt3Brush") });
            row.Children.Add(name);
            row.Children.Add(new TextBlock { Text = "类型", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(type);
            row.Children.Add(req);
            row.Children.Add(new TextBlock { Text = "字典", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(lookup);
            row.Children.Add(core);
            row.Children.Add(new TextBlock { Text = "序", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(order);
            row.Children.Add(del);
            del.Click += (_, _) => { _fields.Remove(f); BuildFieldRows(); };
            _fieldRows.Add((f, name, type, req, lookup, core, order));
            FieldPanel.Children.Add(row);
        }
    }

    private void OnAddField(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedEntity is null) return;
        _fields.Add(new FieldDef
        {
            EntityDefId = _selectedEntity.Id,
            EntityDefCode = _selectedEntity.Code,
            Name = "新字段",
            Type = FieldType.Text,
            IsCore = false,
            DisplayOrder = _fields.Count + 1
        });
        BuildFieldRows();
    }

    private async void OnSaveFields(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedEntity is null) return;
        var ent = _selectedEntity;
        foreach (var (def, name, type, req, lookup, core, order) in _fieldRows)
        {
            def.Name = name.Text?.Trim() ?? "";
            def.Type = (FieldType)(type.SelectedValue ?? 0);
            def.IsRequired = req.IsChecked == true;
            def.LookupDefCode = type.SelectedValue is (int)FieldType.Lookup ? lookup.SelectedValue as string : null;
            def.IsCore = core.IsChecked == true;
            def.DisplayOrder = (int)(order.Value ?? 0);
            def.EntityDefId = ent.Id;
            def.EntityDefCode = ent.Code;
            if (!string.IsNullOrEmpty(def.Name))
                await _metadata.SaveFieldDefAsync(def);
        }
        await LoadFieldsAsync(ent.Code);
    }

    // ---------------- 系统参数（阶段2） ----------------
    private AppDbContext? _pdb;                       // 与 _audit 同源，仅两参构造时有
    private List<SystemParam> _paramsList = new();

    private async Task LoadParamsAsync()
    {
        if (_pdb is null) return;
        _paramsList = await _pdb.SystemParams.OrderBy(p => p.Key).ToListAsync();
        BuildParamRows();
    }

    private void BuildParamRows()
    {
        ParamPanel.Children.Clear();
        _paramRows.Clear();
        foreach (var p in _paramsList)
        {
            var key = new TextBox { Text = p.Key, Width = 160, Padding = new Thickness(8) };
            var value = new TextBox { Text = p.Value, Width = 260, Padding = new Thickness(8) };
            var desc = new TextBox { Text = p.Description ?? "", Width = 260, Padding = new Thickness(8), Watermark = "用途说明（选填）" };
            var del = new Button { Content = "删除" }; del.Classes.Add("danger");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = "键", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(key);
            row.Children.Add(new TextBlock { Text = "值", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Txt3Brush") });
            row.Children.Add(value);
            row.Children.Add(desc);
            row.Children.Add(del);
            del.Click += async (_, _) =>
            {
                if (_params is null) return;
                await _params.DeleteAsync(p.Key);      // 立即落库删除
                await LoadParamsAsync();
            };
            _paramRows.Add((p, key, value, desc));
            ParamPanel.Children.Add(row);
        }
    }

    private void OnAddParam(object? sender, RoutedEventArgs e)
    {
        _paramsList.Add(new SystemParam { Key = "", Value = "", Description = "" });
        BuildParamRows();
    }

    private async void OnSaveParams(object? sender, RoutedEventArgs e)
    {
        if (_params is null) return;
        var seen = new HashSet<string>();
        foreach (var (p, key, value, desc) in _paramRows)
        {
            var k = key.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(k) || !seen.Add(k)) continue;   // 空键跳过、重复键只存第一条
            await _params.SetAsync(k, value.Text?.Trim() ?? "", desc.Text?.Trim());
            if (p.Key != k && !string.IsNullOrEmpty(p.Key)) await _params.DeleteAsync(p.Key);   // 改键 = 删旧建新
        }
        await LoadParamsAsync();
        ParamStatus.Text = "已保存，立即生效";
    }
}
