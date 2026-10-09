using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// M3-A 计划任务调度器：管理任务（名称/间隔/启用），在到期时运行，或手动"立即运行"。
/// 目前任务类型仅 RunAlerts（评估预警规则）；后续可扩展（对账、快照、备份等）。
/// </summary>
public class SchedulerService
{
    private readonly AppDbContext _db;
    private readonly AlertService _alerts;

    public SchedulerService(AppDbContext db, AlertService alerts)
    {
        _db = db;
        _alerts = alerts;
    }

    public Task<List<ScheduledTask>> ListTasksAsync()
        => _db.ScheduledTasks.OrderBy(t => t.Id).ToListAsync();

    public async Task<int> SaveTaskAsync(ScheduledTask task)
    {
        if (task.Id == 0) _db.ScheduledTasks.Add(task);
        else _db.ScheduledTasks.Update(task);
        await _db.SaveChangesAsync();
        return task.Id;
    }

    public async Task DeleteTaskAsync(int id)
    {
        var t = await _db.ScheduledTasks.FindAsync(id);
        if (t is null) return;
        _db.ScheduledTasks.Remove(t);
        await _db.SaveChangesAsync();
    }

    /// <summary>运行全部到期的启用任务，返回本次运行的任务数。</summary>
    public async Task<int> RunDueAsync(DateTime now)
    {
        var due = (await _db.ScheduledTasks.Where(t => t.Enabled).ToListAsync())
            .Where(t => t.LastRunAt is null || now >= t.LastRunAt.Value.AddMinutes(t.IntervalMinutes))
            .ToList();

        foreach (var t in due)
        {
            var n = await RunKindAsync(t.Kind);
            t.LastRunCount = Math.Max(n, 0);
            t.LastRunAt = now;
        }
        if (due.Count > 0) await _db.SaveChangesAsync();
        return due.Count;
    }

    /// <summary>立即运行指定任务，返回其产出的预警数（-1 表示无规则）。</summary>
    public async Task<int> RunNowAsync(int taskId, DateTime now)
    {
        var t = await _db.ScheduledTasks.FindAsync(taskId);
        if (t is null) return -1;
        var n = await RunKindAsync(t.Kind);
        t.LastRunCount = Math.Max(n, 0);
        t.LastRunAt = now;
        await _db.SaveChangesAsync();
        return n;
    }

    private Task<int> RunKindAsync(ScheduledTaskKind kind) => kind switch
    {
        ScheduledTaskKind.RunAlerts => _alerts.EvaluateAsync(),
        _ => Task.FromResult(0)
    };
}
