namespace CostLedger.Domain;

/// <summary>泛型仓储接口——Infrastructure 用 EF Core 实现，预留切换 SQL Server 的抽象边界</summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<T>> ListAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
