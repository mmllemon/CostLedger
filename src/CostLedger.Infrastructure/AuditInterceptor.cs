using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 审计拦截器：在 SaveChanges 时，对"被审计的关键业务实体"的增/改/删自动追加 AuditLog。
/// 集中一处、覆盖所有写入路径（含直接 _db.SaveChangesAsync 的页面）；种子期间由 SuppressAudit 抑制。
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    /// <summary>纳入审计的实体类型（业务台账对象；元数据/字典/审计自身不纳入）。</summary>
    private static readonly HashSet<Type> Audited = new()
    {
        typeof(Contract), typeof(ContractLine),
        typeof(ChangeOrder), typeof(MaterialReceipt), typeof(Invoice), typeof(Settlement),
        typeof(Payment), typeof(Measurement), typeof(CostEntry), typeof(Project), typeof(Party), typeof(CostCategory),
        typeof(AlertRule), typeof(ScheduledTask)
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is not AppDbContext app || app.SuppressAudit) return;

        var logs = new List<AuditLog>();
        var by = Environment.UserName;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog) continue;
            if (!Audited.Contains(entry.Entity.GetType())) continue;

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => (AuditAction?)null
            };
            if (action is null) continue;

            var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
            var id = idProp?.CurrentValue as int? ?? 0;

            logs.Add(new AuditLog
            {
                EntityName = entry.Entity.GetType().Name,
                EntityId = id,
                Action = action.Value,
                After = action == AuditAction.Delete ? null : Snapshot(entry),
                At = DateTime.UtcNow,
                By = by
            });
        }

        if (logs.Count > 0) app.AuditLogs.AddRange(logs);
    }

    /// <summary>标量字段快照（JSON；导航/字节数组略过）。</summary>
    private static string Snapshot(EntityEntry entry)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var p in entry.Properties)
        {
            if (p.Metadata.IsPrimaryKey()) continue;
            if (p.CurrentValue is byte[]) continue;
            dict[p.Metadata.Name] = p.CurrentValue?.ToString();
        }
        return JsonSerializer.Serialize(dict);
    }
}
