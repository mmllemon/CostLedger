using CostLedger.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CostLedger.Infrastructure;

/// <summary>项目主数据服务：新建项目（编号/名称必填、编号唯一）。</summary>
public sealed class ProjectService
{
    private readonly AppDbContext _db;
    public ProjectService(AppDbContext db) => _db = db;

    public Task<System.Collections.Generic.List<Project>> ListAsync(CancellationToken ct = default)
        => _db.Projects.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

    /// <summary>新建项目。编号与名称必填，编号全公司唯一。</summary>
    public async Task<Project> CreateProjectAsync(
        string code, string name, string? ownerName, DateOnly startDate, DateOnly? plannedEndDate,
        string? remark = null, CancellationToken ct = default)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        if (code.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("项目编号与项目名称必填。");
        if (await _db.Projects.AnyAsync(x => x.Code == code, ct))
            throw new InvalidOperationException($"项目编号 {code} 已存在。");

        var project = new Project
        {
            Code = code,
            Name = name,
            OwnerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName.Trim(),
            StartDate = startDate,
            PlannedEndDate = plannedEndDate,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return project;
    }
}
