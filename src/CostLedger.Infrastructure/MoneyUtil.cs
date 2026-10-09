using System;
using System.Collections.Generic;
using System.Text;

namespace CostLedger.Infrastructure;

/// <summary>
/// 金额工具：人民币中文大写（供打印/正式表单使用）。
/// 例：1863000 → 壹佰捌拾陆万叁仟元整；1234.56 → 壹仟贰佰叁拾肆元伍角陆分。
/// </summary>
public static class MoneyUtil
{
    private static readonly string[] Digits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };
    private static readonly string[] Units = { "", "拾", "佰", "仟" };
    private static readonly string[] BigUnits = { "", "万", "亿", "万亿" };

    public static string ToChineseCapital(decimal amount)
    {
        bool negative = amount < 0;
        amount = Math.Abs(decimal.Round(amount, 2, MidpointRounding.AwayFromZero));

        long integer = (long)decimal.Truncate(amount);
        int dec = (int)decimal.Round((amount - integer) * 100m, 0, MidpointRounding.AwayFromZero);
        int jiao = dec / 10;
        int fen = dec % 10;

        var sb = new StringBuilder();
        if (negative) sb.Append("负");

        if (integer > 0)
        {
            sb.Append(ConvertInteger(integer));
            sb.Append("元");
        }
        else
        {
            sb.Append("零元");
        }

        if (jiao == 0 && fen == 0)
        {
            sb.Append("整");
            return sb.ToString();
        }

        if (jiao > 0) sb.Append(Digits[jiao]).Append('角');
        else if (fen > 0) sb.Append('零');   // 如 壹元零伍分

        if (fen > 0) sb.Append(Digits[fen]).Append('分');

        return sb.ToString();
    }

    private static string ConvertInteger(long n)
    {
        if (n == 0) return "零";

        var groups = new List<int>();
        long t = n;
        while (t > 0)
        {
            groups.Add((int)(t % 10000));
            t /= 10000;
        }

        var sb = new StringBuilder();
        bool zeroPending = false;
        for (int g = groups.Count - 1; g >= 0; g--)
        {
            int grp = groups[g];
            if (grp == 0)
            {
                if (sb.Length > 0) zeroPending = true;   // 整组为零，需要时补"零"
                continue;
            }
            if (zeroPending)
            {
                sb.Append('零');
                zeroPending = false;
            }
            sb.Append(ConvertGroup(grp));
            sb.Append(BigUnits[g]);
        }
        return sb.ToString();
    }

    /// <summary>转换 1~9999 的四位组。</summary>
    private static string ConvertGroup(int grp)
    {
        var sb = new StringBuilder();
        bool zeroPending = false;
        int unitIdx = 3;      // 仟
        int div = 1000;
        while (div > 0)
        {
            int d = (grp / div) % 10;
            if (d == 0)
            {
                if (sb.Length > 0) zeroPending = true;
            }
            else
            {
                if (zeroPending)
                {
                    sb.Append('零');
                    zeroPending = false;
                }
                sb.Append(Digits[d]);
                sb.Append(Units[unitIdx]);
            }
            div /= 10;
            unitIdx--;
        }
        return sb.ToString();
    }
}
