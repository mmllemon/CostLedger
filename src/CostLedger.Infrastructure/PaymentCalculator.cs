using System;

namespace CostLedger.Infrastructure;

/// <summary>付款试算结果：本期完成额 → 扣质保金 → 抵预付款 → 净付额。</summary>
public record PaymentBreakdown(decimal Gross, decimal Retention, decimal AdvanceDeduction, decimal Net);

/// <summary>
/// 付款试算器（纯函数）。
/// 本合同参数（质保率/预付款比例/抵扣方式）逐合同传入——2026-10-09 口径：质保/预付款合同级可配，不写死全局默认。
/// 抵扣方式：含"一次"→ 一次性扣完未抵余额；否则按预付款比例逐期抵扣。
/// </summary>
public static class PaymentCalculator
{
    public static PaymentBreakdown Compute(
        decimal gross, decimal retentionRate, decimal advanceRate, decimal advanceOutstanding, string? deductionMethod)
    {
        if (gross <= 0) return new PaymentBreakdown(gross, 0m, 0m, gross);

        var retention = Round(gross * Clamp01(retentionRate));
        var baseForAdvance = gross - retention;
        decimal advance = 0m;

        if (advanceOutstanding > 0 && baseForAdvance > 0)
        {
            var oneOff = !string.IsNullOrEmpty(deductionMethod) && deductionMethod.Contains("一次");
            var perPeriod = oneOff ? advanceOutstanding : Round(baseForAdvance * Clamp01(advanceRate));
            advance = Math.Min(advanceOutstanding, perPeriod);
        }

        var net = gross - retention - advance;
        return new PaymentBreakdown(gross, retention, advance, net);
    }

    private static decimal Clamp01(decimal v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
