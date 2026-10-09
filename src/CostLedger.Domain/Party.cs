namespace CostLedger.Domain;

/// <summary>往来单位（甲方/供方/分包/监理……）</summary>
public class Party
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PartyRole Role { get; set; }
    public string? TaxNo { get; set; }
    public string? Contact { get; set; }

    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
