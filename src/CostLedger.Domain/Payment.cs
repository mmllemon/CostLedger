namespace CostLedger.Domain;

/// <summary>资金付款/收款（四流合一中的资金流，含质保金/预付款抵扣）</summary>
public class Payment
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int PartyId { get; set; }
    public Party? Party { get; set; }
    /// <summary>方向：付款（对下支出）/ 收款（对上收入）</summary>
    public PaymentDirection Direction { get; set; } = PaymentDirection.Pay;
    /// <summary>付款类型：预付款/进度款/结算款/质保返还</summary>
    public PaymentType Type { get; set; } = PaymentType.Progress;
    /// <summary>关联计量期（可选）</summary>
    public int? MeasurementId { get; set; }
    public Measurement? Measurement { get; set; }
    /// <summary>是否含农民工工资（2026-10-09 口径：仅标记，不做专户隔离）</summary>
    public bool ContainsMigrantWage { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }                 // 实付/实收金额（净额）
    public decimal? RetentionDeduction { get; set; }     // 质保金扣留
    public decimal? AdvanceDeduction { get; set; }       // 预付款抵扣
    public string? Remark { get; set; }
}
