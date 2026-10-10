using System;

namespace CostLedger.Domain;

/// <summary>
/// 阶段2 字典启用/停用的可见性规则（纯函数，供渲染层与单测共用）：
/// 下拉只出现启用项；已停用但恰好是当前值的历史数据仍保留显示——否则编辑老记录会静默丢值。
/// </summary>
public static class LookupFilter
{
    public static List<LookupItem> Visible(IReadOnlyList<LookupItem>? items, string? current)
    {
        if (items is null || items.Count == 0) return new List<LookupItem>();
        var visible = items.Where(i => i.IsEnabled).ToList();
        if (!string.IsNullOrEmpty(current) && visible.All(i => i.Code != current))
        {
            var cur = items.FirstOrDefault(i => i.Code == current);
            if (cur != null) visible.Add(cur);
        }
        return visible;
    }
}
