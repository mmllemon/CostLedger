using Avalonia.Collections;
using CostLedger.App.ViewModels;
using CostLedger.Domain;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.App.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private readonly ICostSpineService _spine;
    private string _projectName = "";
    private AvaloniaList<CostSpineLine> _lines = new();
    private decimal _totalTarget;
    private decimal _totalIncurred;
    private decimal _totalVariance;

    public DashboardViewModel(ICostSpineService spine) => _spine = spine;

    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }
    public AvaloniaList<CostSpineLine> Lines { get => _lines; set => SetProperty(ref _lines, value); }
    public decimal TotalTarget { get => _totalTarget; set => SetProperty(ref _totalTarget, value); }
    public decimal TotalIncurred { get => _totalIncurred; set => SetProperty(ref _totalIncurred, value); }
    public decimal TotalVariance { get => _totalVariance; set => SetProperty(ref _totalVariance, value); }

    public async Task LoadAsync()
    {
        // M1 演示项目 Id = 1（种子数据）
        var lines = await _spine.GetSpineAsync(1);
        Lines = new AvaloniaList<CostSpineLine>(lines);
        TotalTarget = lines.Sum(l => l.Target);
        TotalIncurred = lines.Sum(l => l.Incurred);
        TotalVariance = lines.Sum(l => l.Variance);
        ProjectName = "残疾人项目 10KV 外线电缆工程";
    }
}
