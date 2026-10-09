using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.Views;

/// <summary>
/// M2-E2 业务单据录入界面：变更令 / 材料收料 / 发票 / 竣工结算。
/// 变更令确认 → 计入目标成本；材料收料乙供确认 → 回写已发生；发票/结算仅登记。
/// </summary>
public partial class BusinessDocsView : UserControl
{
    private const int DefaultProjectId = 1;

    private readonly AppDbContext _db = null!;
    private readonly ChangeOrderService _coSvc = null!;
    private readonly MaterialReceiptService _mrSvc = null!;
    private readonly InvoiceService _invSvc = null!;
    private readonly SettlementService _stSvc = null!;
    private readonly PaymentService _paySvc = null!;

    private List<Project> _projects = new();
    private List<Contract> _contracts = new();
    private List<CostCategory> _categories = new();
    private List<Party> _parties = new();

    private int _coId, _mrId, _invId, _stId, _payId;

    public BusinessDocsView()
    {
        InitializeComponent();
    }

    public BusinessDocsView(AppDbContext db) : this()
    {
        _db = db;
        _coSvc = new ChangeOrderService(db);
        _mrSvc = new MaterialReceiptService(db, new CostWriteBackService(db));
        _invSvc = new InvoiceService(db);
        _stSvc = new SettlementService(db);
        _paySvc = new PaymentService(db);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _projects = await _db.Projects.ToListAsync();
        _contracts = await _db.Contracts.OrderBy(c => c.Code).ToListAsync();
        _categories = await _db.CostCategories.OrderBy(c => c.Name).ToListAsync();
        _parties = await _db.Parties.OrderBy(p => p.Name).ToListAsync();

        foreach (var cb in new[] { CoProject, MrProject, InvProject, StProject })
            Wire(cb, _projects, "Name");
        foreach (var cb in new[] { CoContract, MrContract, InvContract, StContract, PayContract })
            Wire(cb, _contracts, "Code");
        foreach (var cb in new[] { CoCategory, MrCategory })
            Wire(cb, _categories, "Name");
        Wire(InvParty, _parties, "Name");
        Wire(PayParty, _parties, "Name");

        CoType.ItemsSource = Enum.GetNames<ChangeOrderType>();
        MrSource.ItemsSource = new[] { "乙供（我方采购）", "甲供（甲方供应）" };
        InvDirection.ItemsSource = new[] { "进项", "销项" };
        InvCategory.ItemsSource = new[] { "专用发票", "普通发票" };
        StStatus.ItemsSource = new[] { "草稿", "已审定" };
        PayDirection.ItemsSource = new[] { "付款", "收款" };
        PayType.ItemsSource = new[] { "预付款", "进度款", "结算款", "质保返还" };

        await ReloadCoAsync();
        await ReloadMrAsync();
        await ReloadInvAsync();
        await ReloadStAsync();
        await ReloadPayAsync();
    }

    private static void Wire(ComboBox cb, System.Collections.IEnumerable items, string member)
    {
        cb.ItemsSource = items;
        cb.DisplayMemberBinding = new Binding(member);
    }

    private static DateOnly AsDate(DatePicker dp) =>
        dp.SelectedDate.HasValue ? DateOnly.FromDateTime(dp.SelectedDate.Value.DateTime) : DateOnly.FromDateTime(DateTime.Today);

    private static void SetDate(DatePicker dp, DateOnly d) => dp.SelectedDate = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue));

    private static decimal Val(NumericUpDown n) => n.Value ?? 0m;

    private static T? Pick<T>(ComboBox cb) where T : class => cb.SelectedItem as T;

    // ==================== 变更令 ====================
    private async Task ReloadCoAsync(int selectId = 0)
    {
        var list = await _coSvc.ListAsync(DefaultProjectId);
        CoList.ItemsSource = list;
        CoList.DisplayMemberBinding = new Binding("Code");
        CoList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
    }

    private void OnCoSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (CoList.SelectedItem is not ChangeOrder co) return;
        _coId = co.Id;
        CoCode.Text = co.Code;
        CoType.SelectedItem = co.Type.ToString();
        CoProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == co.ProjectId);
        CoContract.SelectedItem = _contracts.FirstOrDefault(c => c.Id == co.ContractId);
        CoCategory.SelectedItem = _categories.FirstOrDefault(c => c.Id == co.CostCategoryId);
        CoAmount.Value = co.Amount;
        CoTitle.Text = co.Title;
        SetDate(CoDate, co.OrderDate);
        CoNote.Text = co.Note;
        CoStatus.Text = co.Status == DocStatus.Confirmed ? "已确认（已计入目标）" : "草稿";
    }

    private void OnCoNew(object? sender, RoutedEventArgs e)
    {
        _coId = 0;
        CoCode.Text = "";
        CoType.SelectedIndex = 0;
        CoProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == DefaultProjectId);
        CoContract.SelectedItem = null;
        CoCategory.SelectedItem = null;
        CoAmount.Value = 0;
        CoTitle.Text = "";
        SetDate(CoDate, DateOnly.FromDateTime(DateTime.Today));
        CoNote.Text = "";
        CoStatus.Text = "草稿";
        CoList.SelectedItem = null;
    }

    private async void OnCoSave(object? sender, RoutedEventArgs e)
    {
        var co = _coId == 0 ? new ChangeOrder() : await _db.ChangeOrders.FindAsync(_coId) ?? new ChangeOrder();
        co.Code = CoCode.Text?.Trim() ?? "";
        co.Type = Enum.TryParse<ChangeOrderType>(CoType.SelectedItem as string, out var t) ? t : ChangeOrderType.DesignChange;
        co.ProjectId = Pick<Project>(CoProject)?.Id ?? DefaultProjectId;
        co.ContractId = Pick<Contract>(CoContract)?.Id;
        co.CostCategoryId = Pick<CostCategory>(CoCategory)?.Id;
        co.Amount = Val(CoAmount);
        co.Title = CoTitle.Text?.Trim() ?? "";
        co.OrderDate = AsDate(CoDate);
        co.Note = CoNote.Text;
        var id = await _coSvc.SaveAsync(co);
        await ReloadCoAsync(id);
    }

    private async void OnCoConfirm(object? sender, RoutedEventArgs e)
    {
        if (_coId == 0) return;
        await _coSvc.ConfirmAsync(_coId);
        await ReloadCoAsync(_coId);
    }

    private async void OnCoReopen(object? sender, RoutedEventArgs e)
    {
        if (_coId == 0) return;
        await _coSvc.ReopenAsync(_coId);
        await ReloadCoAsync(_coId);
    }

    private async void OnCoDelete(object? sender, RoutedEventArgs e)
    {
        if (_coId == 0) return;
        await _coSvc.DeleteAsync(_coId);
        _coId = 0;
        await ReloadCoAsync();
        OnCoNew(null, new RoutedEventArgs());
    }

    // ==================== 材料收料 ====================
    private async Task ReloadMrAsync(int selectId = 0)
    {
        var list = await _mrSvc.ListAsync(DefaultProjectId);
        MrList.ItemsSource = list;
        MrList.DisplayMemberBinding = new Binding("Code");
        MrList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
    }

    private void OnMrSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (MrList.SelectedItem is not MaterialReceipt mr) return;
        _mrId = mr.Id;
        MrCode.Text = mr.Code;
        MrSource.SelectedIndex = mr.Source == MaterialSource.Owner ? 1 : 0;
        MrProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == mr.ProjectId);
        MrContract.SelectedItem = _contracts.FirstOrDefault(c => c.Id == mr.ContractId);
        MrCategory.SelectedItem = _categories.FirstOrDefault(c => c.Id == mr.CostCategoryId);
        MrName.Text = mr.MaterialName;
        MrSpec.Text = mr.Spec;
        MrUnit.Text = mr.Unit;
        MrQty.Value = mr.Quantity;
        MrPrice.Value = mr.UnitPrice;
        MrAmount.Value = mr.Amount;
        SetDate(MrDate, mr.ReceiptDate);
        MrStatus.Text = mr.Status == DocStatus.Confirmed ? "已确认" : "草稿";
    }

    private void OnMrNew(object? sender, RoutedEventArgs e)
    {
        _mrId = 0;
        MrCode.Text = "";
        MrSource.SelectedIndex = 0;
        MrProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == DefaultProjectId);
        MrContract.SelectedItem = null;
        MrCategory.SelectedItem = null;
        MrName.Text = ""; MrSpec.Text = ""; MrUnit.Text = "";
        MrQty.Value = 0; MrPrice.Value = 0; MrAmount.Value = 0;
        SetDate(MrDate, DateOnly.FromDateTime(DateTime.Today));
        MrStatus.Text = "草稿";
        MrList.SelectedItem = null;
    }

    private async void OnMrSave(object? sender, RoutedEventArgs e)
    {
        var mr = _mrId == 0 ? new MaterialReceipt() : await _db.MaterialReceipts.FindAsync(_mrId) ?? new MaterialReceipt();
        mr.Code = MrCode.Text?.Trim() ?? "";
        mr.Source = MrSource.SelectedIndex == 1 ? MaterialSource.Owner : MaterialSource.Self;
        mr.ProjectId = Pick<Project>(MrProject)?.Id ?? DefaultProjectId;
        mr.ContractId = Pick<Contract>(MrContract)?.Id;
        mr.CostCategoryId = Pick<CostCategory>(MrCategory)?.Id;
        mr.MaterialName = MrName.Text?.Trim() ?? "";
        mr.Spec = MrSpec.Text; mr.Unit = MrUnit.Text;
        mr.Quantity = Val(MrQty);
        mr.UnitPrice = Val(MrPrice);
        mr.Amount = Val(MrAmount);
        mr.ReceiptDate = AsDate(MrDate);
        var id = await _mrSvc.SaveAsync(mr);
        await ReloadMrAsync(id);
    }

    private async void OnMrConfirm(object? sender, RoutedEventArgs e)
    {
        if (_mrId == 0) return;
        await _mrSvc.ConfirmAsync(_mrId);
        await ReloadMrAsync(_mrId);
    }

    private async void OnMrDelete(object? sender, RoutedEventArgs e)
    {
        if (_mrId == 0) return;
        await _mrSvc.DeleteAsync(_mrId);
        _mrId = 0;
        await ReloadMrAsync();
        OnMrNew(null, new RoutedEventArgs());
    }

    // ==================== 发票 ====================
    private async Task ReloadInvAsync(int selectId = 0)
    {
        var list = await _invSvc.ListAsync(DefaultProjectId);
        InvList.ItemsSource = list;
        InvList.DisplayMemberBinding = new Binding("Code");
        InvList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
    }

    private void OnInvSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (InvList.SelectedItem is not Invoice inv) return;
        _invId = inv.Id;
        InvCode.Text = inv.Code;
        InvDirection.SelectedIndex = inv.Direction == InvoiceDirection.Output ? 1 : 0;
        InvCategory.SelectedIndex = inv.Category == InvoiceCategory.Normal ? 1 : 0;
        InvProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == inv.ProjectId);
        InvContract.SelectedItem = _contracts.FirstOrDefault(c => c.Id == inv.ContractId);
        InvParty.SelectedItem = _parties.FirstOrDefault(p => p.Id == inv.PartyId);
        InvRate.Value = inv.TaxRate;
        InvNet.Value = inv.NetAmount;
        InvTax.Value = inv.TaxAmount;
        InvTotal.Value = inv.TotalAmount;
        SetDate(InvDate, inv.InvoiceDate);
        InvStatus.Text = inv.Status;
    }

    private void OnInvNew(object? sender, RoutedEventArgs e)
    {
        _invId = 0;
        InvCode.Text = "";
        InvDirection.SelectedIndex = 0;
        InvCategory.SelectedIndex = 0;
        InvProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == DefaultProjectId);
        InvContract.SelectedItem = null;
        InvParty.SelectedItem = null;
        InvRate.Value = 0.13m; InvNet.Value = 0; InvTax.Value = 0; InvTotal.Value = 0;
        SetDate(InvDate, DateOnly.FromDateTime(DateTime.Today));
        InvStatus.Text = "已收票";
        InvList.SelectedItem = null;
    }

    private async void OnInvSave(object? sender, RoutedEventArgs e)
    {
        var inv = _invId == 0 ? new Invoice() : await _db.Invoices.FindAsync(_invId) ?? new Invoice();
        inv.Code = InvCode.Text?.Trim() ?? "";
        inv.Direction = InvDirection.SelectedIndex == 1 ? InvoiceDirection.Output : InvoiceDirection.Input;
        inv.Category = InvCategory.SelectedIndex == 1 ? InvoiceCategory.Normal : InvoiceCategory.Special;
        inv.ProjectId = Pick<Project>(InvProject)?.Id ?? DefaultProjectId;
        inv.ContractId = Pick<Contract>(InvContract)?.Id;
        inv.PartyId = Pick<Party>(InvParty)?.Id;
        inv.TaxRate = Val(InvRate);
        inv.NetAmount = Val(InvNet);
        inv.TaxAmount = Val(InvTax);
        inv.TotalAmount = Val(InvTotal);
        inv.InvoiceDate = AsDate(InvDate);
        inv.Status = InvStatus.Text;
        var id = await _invSvc.SaveAsync(inv);
        await ReloadInvAsync(id);
    }

    private async void OnInvDelete(object? sender, RoutedEventArgs e)
    {
        if (_invId == 0) return;
        await _invSvc.DeleteAsync(_invId);
        _invId = 0;
        await ReloadInvAsync();
        OnInvNew(null, new RoutedEventArgs());
    }

    // ==================== 竣工结算 ====================
    private async Task ReloadStAsync(int selectId = 0)
    {
        var list = await _stSvc.ListAsync(DefaultProjectId);
        StList.ItemsSource = list;
        StList.DisplayMemberBinding = new Binding("Code");
        StList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
    }

    private void OnStSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (StList.SelectedItem is not Settlement s) return;
        _stId = s.Id;
        StCode.Text = s.Code;
        StStatus.SelectedIndex = s.Status == DocStatus.Confirmed ? 1 : 0;
        StProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == s.ProjectId);
        StContract.SelectedItem = _contracts.FirstOrDefault(c => c.Id == s.ContractId);
        StOriginal.Value = s.OriginalAmount;
        StAdjust.Value = s.AdjustAmount;
        StSettled.Value = s.SettledAmount;
        SetDate(StDate, s.SettleDate);
        StNote.Text = s.Note;
    }

    private void OnStNew(object? sender, RoutedEventArgs e)
    {
        _stId = 0;
        StCode.Text = "";
        StStatus.SelectedIndex = 0;
        StProject.SelectedItem = _projects.FirstOrDefault(p => p.Id == DefaultProjectId);
        StContract.SelectedItem = null;
        StOriginal.Value = 0; StAdjust.Value = 0; StSettled.Value = 0;
        SetDate(StDate, DateOnly.FromDateTime(DateTime.Today));
        StNote.Text = "";
        StList.SelectedItem = null;
    }

    private async void OnStSave(object? sender, RoutedEventArgs e)
    {
        var s = _stId == 0 ? new Settlement() : await _db.Settlements.FindAsync(_stId) ?? new Settlement();
        s.Code = StCode.Text?.Trim() ?? "";
        s.Status = StStatus.SelectedIndex == 1 ? DocStatus.Confirmed : DocStatus.Draft;
        s.ProjectId = Pick<Project>(StProject)?.Id ?? DefaultProjectId;
        s.ContractId = Pick<Contract>(StContract)?.Id;
        s.OriginalAmount = Val(StOriginal);
        s.AdjustAmount = Val(StAdjust);
        s.SettledAmount = Val(StSettled);
        s.SettleDate = AsDate(StDate);
        s.Note = StNote.Text;
        var id = await _stSvc.SaveAsync(s);
        await ReloadStAsync(id);
    }

    private async void OnStDelete(object? sender, RoutedEventArgs e)
    {
        if (_stId == 0) return;
        await _stSvc.DeleteAsync(_stId);
        _stId = 0;
        await ReloadStAsync();
        OnStNew(null, new RoutedEventArgs());
    }

    // ==================== 收付款（资金流） ====================
    private static readonly PaymentDirection[] PayDirs = { PaymentDirection.Pay, PaymentDirection.Receive };
    private static readonly PaymentType[] PayTypes = { PaymentType.Advance, PaymentType.Progress, PaymentType.Settlement, PaymentType.Retention };

    private async Task ReloadPayAsync(int selectId = 0)
    {
        var list = await _paySvc.ListAsync(DefaultProjectId);
        PayList.ItemsSource = list;
        PayList.DisplayMemberBinding = new Binding("Code");
        PayList.SelectedItem = selectId > 0 ? list.FirstOrDefault(x => x.Id == selectId) : list.FirstOrDefault();
    }

    private void OnPaySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PayList.SelectedItem is not Payment p) return;
        _payId = p.Id;
        PayCode.Text = p.Code;
        PayDirection.SelectedIndex = Array.IndexOf(PayDirs, p.Direction);
        PayType.SelectedIndex = Array.IndexOf(PayTypes, p.Type);
        PayContract.SelectedItem = _contracts.FirstOrDefault(c => c.Id == p.ContractId);
        PayParty.SelectedItem = _parties.FirstOrDefault(x => x.Id == p.PartyId);
        PayAmount.Value = p.Amount;
        PayMigrant.IsChecked = p.ContainsMigrantWage;
        SetDate(PayDate, p.Date);
        PayRemark.Text = p.Remark;
    }

    private void OnPayNew(object? sender, RoutedEventArgs e)
    {
        _payId = 0;
        PayCode.Text = "";
        PayDirection.SelectedIndex = 0;
        PayType.SelectedIndex = 1;
        PayContract.SelectedItem = null;
        PayParty.SelectedItem = null;
        PayAmount.Value = 0;
        PayMigrant.IsChecked = false;
        SetDate(PayDate, DateOnly.FromDateTime(DateTime.Today));
        PayRemark.Text = "";
        PayList.SelectedItem = null;
    }

    private async void OnPaySave(object? sender, RoutedEventArgs e)
    {
        var p = _payId == 0 ? new Payment() : await _db.Payments.FindAsync(_payId) ?? new Payment();
        p.Code = PayCode.Text?.Trim() ?? "";
        p.Direction = PayDirs[Math.Clamp(PayDirection.SelectedIndex, 0, 1)];
        p.Type = PayTypes[Math.Clamp(PayType.SelectedIndex, 0, 3)];
        p.ContractId = Pick<Contract>(PayContract)?.Id;
        p.PartyId = Pick<Party>(PayParty)?.Id ?? _parties.FirstOrDefault()?.Id ?? 0;
        p.Amount = Val(PayAmount);
        p.ContainsMigrantWage = PayMigrant.IsChecked == true;
        p.Date = AsDate(PayDate);
        p.Remark = PayRemark.Text;
        var id = await _paySvc.SaveAsync(p);
        await ReloadPayAsync(id);
    }

    private async void OnPayDelete(object? sender, RoutedEventArgs e)
    {
        if (_payId == 0) return;
        await _paySvc.DeleteAsync(_payId);
        _payId = 0;
        await ReloadPayAsync();
        OnPayNew(null, new RoutedEventArgs());
    }
}
