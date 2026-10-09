using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

public partial class MasterDataView : UserControl
{
    private readonly AppDbContext _db = null!;
    private readonly MetadataService _metadata = null!;
    private List<LookupItem> _roleItems = new();
    private List<LookupItem> _typeItems = new();
    private List<LookupItem> _taxItems = new();

    private int _currentPartyId;
    private int _currentContractId;

    public MasterDataView()
    {
        InitializeComponent();
    }

    public MasterDataView(AppDbContext db, MetadataService metadata) : this()
    {
        _db = db;
        _metadata = metadata;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _roleItems = await _metadata.ListLookupItemsAsync("PartyRole");
        _typeItems = await _metadata.ListLookupItemsAsync("ContractType");
        _taxItems = await _metadata.ListLookupItemsAsync("TaxRate");

        WireCombo(PartyRole, _roleItems);
        WireCombo(ConType, _typeItems);
        WireCombo(ConTax, _taxItems);
        ConDeduction.ItemsSource = new[] { "按比例抵扣", "一次性抵扣" };
        ConDeduction.SelectedIndex = 0;

        PartyList.ItemsSource = await _db.Parties.OrderBy(p => p.Name).ToListAsync();
        PartyList.DisplayMemberBinding = new Binding("Name");
        ContractList.ItemsSource = await _db.Contracts.OrderBy(c => c.Code).ToListAsync();
        ContractList.DisplayMemberBinding = new Binding("Code");

        if (PartyList.ItemsSource is System.Collections.IList pl && pl.Count > 0)
            PartyList.SelectedIndex = 0;
    }

    private static void WireCombo(ComboBox cb, List<LookupItem> items)
    {
        cb.ItemsSource = items;
        cb.DisplayMemberBinding = new Binding("Name");
        cb.SelectedValueBinding = new Binding("Code");
        cb.SelectedValue = items.Count > 0 ? items[0].Code : null;
    }

    // ---------------- 往来单位 ----------------
    private async void OnPartySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PartyList.SelectedItem is not Party p) return;
        _currentPartyId = p.Id;
        PartyName.Text = p.Name;
        PartyRole.SelectedValue = p.RoleCode;
        PartyTaxNo.Text = p.TaxNo;
        PartyContact.Text = p.Contact;
        await PartyExt.LoadAsync("Party", p.Id);
    }

    private void OnPartyNew(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentPartyId = 0;
        PartyName.Text = "";
        PartyRole.SelectedValue = _roleItems.Count > 0 ? _roleItems[0].Code : null;
        PartyTaxNo.Text = "";
        PartyContact.Text = "";
        PartyList.SelectedItem = null;
        _ = PartyExt.LoadAsync("Party", 0);
    }

    private async void OnPartySave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Party? p = _currentPartyId == 0 ? null : await _db.Parties.FindAsync(_currentPartyId);
        if (p is null)
        {
            p = new Party();
            _db.Parties.Add(p);
        }
        p.Name = PartyName.Text?.Trim() ?? "";
        p.RoleCode = PartyRole.SelectedValue as string ?? "";
        p.TaxNo = PartyTaxNo.Text;
        p.Contact = PartyContact.Text;
        await _db.SaveChangesAsync();

        if (_currentPartyId == 0)
        {
            _currentPartyId = p.Id;
            PartyExt.SetOwnerId(p.Id);
        }
        await PartyExt.SaveAsync();

        PartyList.ItemsSource = await _db.Parties.OrderBy(x => x.Name).ToListAsync();
        PartyList.SelectedItem = p;
    }

    private async void OnPartyDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentPartyId == 0) return;
        var p = await _db.Parties.FindAsync(_currentPartyId);
        if (p is null) return;
        _db.Parties.Remove(p);
        await _db.SaveChangesAsync();
        _currentPartyId = 0;
        PartyList.ItemsSource = await _db.Parties.OrderBy(x => x.Name).ToListAsync();
    }

    // ---------------- 合同 ----------------
    private async void OnContractSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ContractList.SelectedItem is not Contract c) return;
        _currentContractId = c.Id;
        ConCode.Text = c.Code;
        ConType.SelectedValue = c.TypeCode;
        ConTitle.Text = c.Title;
        ConAmount.Value = c.Amount;
        ConTax.SelectedValue = c.TaxRate switch { 0.13m => "T13", 0.09m => "T09", 0.03m => "T03", 0.06m => "T06", _ => _taxItems.Count > 0 ? _taxItems[0].Code : null };
        ConSignDate.SelectedDate = new DateTimeOffset(c.SignDate.ToDateTime(TimeOnly.MinValue));
        ConStatus.Text = c.Status;
        ConRetention.Value = c.RetentionRate;
        ConAdvance.Value = c.AdvanceRate;
        ConDeduction.SelectedItem = string.IsNullOrEmpty(c.AdvanceDeductionMethod) ? "按比例抵扣" : c.AdvanceDeductionMethod;
        await ConExt.LoadAsync("Contract", c.Id);
    }

    private void OnContractNew(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentContractId = 0;
        ConCode.Text = "";
        ConType.SelectedValue = _typeItems.Count > 0 ? _typeItems[0].Code : null;
        ConTitle.Text = "";
        ConAmount.Value = 0;
        ConTax.SelectedValue = _taxItems.Count > 0 ? _taxItems[0].Code : null;
        ConSignDate.SelectedDate = null;
        ConStatus.Text = "执行中";
        ConRetention.Value = 0;
        ConAdvance.Value = 0;
        ConDeduction.SelectedIndex = 0;
        ContractList.SelectedItem = null;
        _ = ConExt.LoadAsync("Contract", 0);
    }

    private async void OnContractSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Contract? c = _currentContractId == 0 ? null : await _db.Contracts.FindAsync(_currentContractId);
        if (c is null)
        {
            c = new Contract { ProjectId = 1 };
            _db.Contracts.Add(c);
        }
        c.Code = ConCode.Text?.Trim() ?? "";
        c.TypeCode = ConType.SelectedValue as string ?? "";
        c.Title = ConTitle.Text;
        c.Amount = ConAmount.Value ?? 0;
        c.TaxRate = (ConTax.SelectedValue as string) switch { "T13" => 0.13m, "T09" => 0.09m, "T03" => 0.03m, "T06" => 0.06m, _ => 0.09m };
        c.SignDate = ConSignDate.SelectedDate.HasValue
            ? DateOnly.FromDateTime(ConSignDate.SelectedDate.Value.DateTime)
            : DateOnly.FromDateTime(DateTime.Today);
        c.Status = ConStatus.Text;
        c.RetentionRate = ConRetention.Value ?? 0m;
        c.AdvanceRate = ConAdvance.Value ?? 0m;
        c.AdvanceDeductionMethod = ConDeduction.SelectedItem as string;
        await _db.SaveChangesAsync();

        if (_currentContractId == 0)
        {
            _currentContractId = c.Id;
            ConExt.SetOwnerId(c.Id);
        }
        await ConExt.SaveAsync();

        ContractList.ItemsSource = await _db.Contracts.OrderBy(x => x.Code).ToListAsync();
        ContractList.SelectedItem = c;
    }

    private async void OnContractDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentContractId == 0) return;
        var c = await _db.Contracts.FindAsync(_currentContractId);
        if (c is null) return;
        _db.Contracts.Remove(c);
        await _db.SaveChangesAsync();
        _currentContractId = 0;
        ContractList.ItemsSource = await _db.Contracts.OrderBy(x => x.Code).ToListAsync();
    }
}
