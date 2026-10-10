namespace CostLedger.Domain;

/// <summary>往来单位（甲方/供方/分包/监理……）</summary>
public class Party
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>往来单位角色代码，引用 LookupDef "PartyRole" 的 LookupItem.Code（字典驱动，取代原硬编码枚举）</summary>
    public string RoleCode { get; set; } = string.Empty;
    public string? TaxNo { get; set; }
    public string? Contact { get; set; }

    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<ProjectPartyRelationship> ProjectRelationships { get; set; } = new List<ProjectPartyRelationship>();
}
