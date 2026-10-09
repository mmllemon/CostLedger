using System;

namespace CostLedger.Domain;

/// <summary>审计动作</summary>
public enum AuditAction { Create, Update, Delete }

/// <summary>
/// 审计日志（只追加，不可改）：记录关键业务实体的 增/改/删。
/// 由 AuditInterceptor 在 SaveChanges 时自动写入，业务代码无需关心。
/// 定位：台账的"谁在何时改了什么"可追溯性（非审批）。
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    /// <summary>实体类型名（如 ChangeOrder）</summary>
    public string EntityName { get; set; } = string.Empty;
    /// <summary>实体主键（新增时为 0）</summary>
    public int EntityId { get; set; }
    public AuditAction Action { get; set; }
    /// <summary>变更后的标量字段快照（JSON；删除时为空）</summary>
    public string? After { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    /// <summary>操作人（本机用户名）</summary>
    public string? By { get; set; }
}
