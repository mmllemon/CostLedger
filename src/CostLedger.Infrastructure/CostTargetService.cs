using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>
/// 大类级目标成本服务：按"成本科目（大类）"直接录入目标金额（不依赖清单）。
/// 用于配电项目按大类（变压器/柜/电缆/箱/安装劳务/土建劳务…）管成本。
/// </summary>
public class CostTargetService
{
    private readonly AppDbContext _db;
    public CostTargetService(AppDbContext db) => _db = db;

    public Task<List<CostTarget>> ListAsync(int projectId)
        => _db.CostTargets.Where(t => t.ProjectId == projectId).ToListAsync();

    /// <summary>按（项目, 科目）新增或更新目标额。</summary>
    public async Task SetAsync(int projectId, int categoryId, decimal amount, string? note)
    {
        var row = await _db.CostTargets.FirstOrDefaultAsync(t => t.ProjectId == projectId && t.CostCategoryId == categoryId);
        if (row is null)
        {
            _db.CostTargets.Add(new CostTarget { ProjectId = projectId, CostCategoryId = categoryId, Amount = amount, Note = note });
        }
        else
        {
            row.Amount = amount;
            row.Note = note;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>删除某（项目, 科目）的目标（用于"清空"）。</summary>
    public async Task ClearAsync(int projectId, int categoryId)
    {
        var rows = await _db.CostTargets.Where(t => t.ProjectId == projectId && t.CostCategoryId == categoryId).ToListAsync();
        if (rows.Count == 0) return;
        _db.CostTargets.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }
}
