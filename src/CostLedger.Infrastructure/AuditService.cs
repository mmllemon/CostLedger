using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>审计查询：按实体/关键字读最近的审计日志（只读）。</summary>
public class AuditService
{
    private readonly AppDbContext _db;
    public AuditService(AppDbContext db) => _db = db;

    public async Task<List<AuditLog>> ListAsync(string? keyword = null, int take = 500)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(a => a.EntityName.Contains(keyword) || (a.By != null && a.By.Contains(keyword)));
        return await q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(take).ToListAsync();
    }
}
