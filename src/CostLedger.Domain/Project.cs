namespace CostLedger.Domain;

/// <summary>工程项目——成本归集的顶层维度</summary>
public class Project
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? PlannedEndDate { get; set; }
    public string? Remark { get; set; }

    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
    public ICollection<CostEntry> CostEntries { get; set; } = new List<CostEntry>();
    public ICollection<ProjectPartyRelationship> PartyRelationships { get; set; } = new List<ProjectPartyRelationship>();
    public ICollection<CostControlItem> CostControlItems { get; set; } = new List<CostControlItem>();
    public ICollection<ExternalAmountFact> ExternalAmountFacts { get; set; } = new List<ExternalAmountFact>();
    public ICollection<CostForecastVersion> CostForecastVersions { get; set; } = new List<CostForecastVersion>();
    public ICollection<ChangeItem> ChangeItems { get; set; } = new List<ChangeItem>();
}
