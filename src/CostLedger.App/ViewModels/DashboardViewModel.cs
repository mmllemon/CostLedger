using Avalonia.Collections;
using CostLedger.App.ViewModels;
using CostLedger.Domain;
using CostLedger.Infrastructure;
using System.Threading.Tasks;

namespace CostLedger.App.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private readonly ICostSpineService _spine;
    private readonly DashboardService _dashboard;
    private string _projectName = "";
    private AvaloniaList<CostSpineLine> _lines = new();
    private AvaloniaList<KpiCard> _cards = new();

    public DashboardViewModel(ICostSpineService spine, DashboardService dashboard)
    {
        _spine = spine;
        _dashboard = dashboard;
    }

    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }
    public AvaloniaList<CostSpineLine> Lines { get => _lines; set => SetProperty(ref _lines, value); }

    /// <summary>看板 KPI 卡片（内容由「看板设计」配置：显隐/顺序/阈值）</summary>
    public AvaloniaList<KpiCard> Cards { get => _cards; set => SetProperty(ref _cards, value); }

    public async Task LoadAsync()
    {
        // M1 演示项目 Id = 1（种子数据）
        var lines = await _spine.GetSpineAsync(1);
        Lines = new AvaloniaList<CostSpineLine>(lines);
        Cards = new AvaloniaList<KpiCard>(await _dashboard.GetCardsAsync(1));
        ProjectName = "残疾人项目 10KV 外线电缆工程";
    }
}
