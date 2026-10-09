namespace CostLedger.Domain;

/// <summary>资金付款（四流合一中的资金流，含质保金/预付款抵扣）</summary>
public class Payment
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int PartyId { get; set; }
    public Party? Party { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }                 // 实付金额
    public decimal? RetentionDeduction { get; set; }     // 质保金抵扣
    public decimal? AdvanceDeduction { get; set; }       // 预付款抵扣
    public string? Remark { get; set; }
}
