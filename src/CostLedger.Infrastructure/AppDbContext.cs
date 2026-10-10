using CostLedger.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.IO;
using System.Linq;

namespace CostLedger.Infrastructure;

/// <summary>应用数据库上下文（M1：SQLite；Repository 抽象预留切换 SQL Server）</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractLine> ContractLines => Set<ContractLine>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<MeasurementLine> MeasurementLines => Set<MeasurementLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CostCategory> CostCategories => Set<CostCategory>();
    public DbSet<CostEntry> CostEntries => Set<CostEntry>();

    // M2-A 元数据层（配置中心地基）
    public DbSet<LookupDef> LookupDefs => Set<LookupDef>();
    public DbSet<LookupItem> LookupItems => Set<LookupItem>();
    public DbSet<EntityDef> EntityDefs => Set<EntityDef>();
    public DbSet<FieldDef> FieldDefs => Set<FieldDef>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();

    // 阶段1：运行时自建对象（与 Directus introspection 对齐的"深度自定义"杀手锏）
    public DbSet<DynamicRecord> DynamicRecords => Set<DynamicRecord>();
    public DbSet<DynamicValue> DynamicValues => Set<DynamicValue>();

    // 阶段2：系统参数（运行时 key-value 配置）
    public DbSet<SystemParam> SystemParams => Set<SystemParam>();

    // M2-C 报表层
    public DbSet<ReportDef> ReportDefs => Set<ReportDef>();
    public DbSet<ReportColumn> ReportColumns => Set<ReportColumn>();
    public DbSet<ReportFilter> ReportFilters => Set<ReportFilter>();

    // M2-D 看板层
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();

    // M2-E 业务单据层（P0 缺口）
    public DbSet<ChangeOrder> ChangeOrders => Set<ChangeOrder>();
    public DbSet<MaterialReceipt> MaterialReceipts => Set<MaterialReceipt>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Settlement> Settlements => Set<Settlement>();

    // M3-A 预警与调度层
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertRecord> AlertRecords => Set<AlertRecord>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();

    // 审计（只追加）
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // 成本管理内核：部位(WBS) 与 工程量清单(BOQ)
    public DbSet<WBSItem> WbsItems => Set<WBSItem>();
    public DbSet<BoqLine> BoqLines => Set<BoqLine>();
    /// <summary>大类级目标成本（按科目直接录）</summary>
    public DbSet<CostTarget> CostTargets => Set<CostTarget>();

    /// <summary>为 true 时不写审计（用于种子/系统初始化）。</summary>
    public bool SuppressAudit { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new AuditInterceptor());

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // 全局 decimal(18,2)，避免 SQLite 默认无精度
        foreach (var entity in mb.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                prop.SetPrecision(18);
                prop.SetScale(2);
            }
        }

        mb.Entity<Project>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.Property(x => x.Name).HasMaxLength(120);
        });

        mb.Entity<Party>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120);
            b.Property(x => x.RoleCode).HasMaxLength(32);
            b.HasIndex(x => x.RoleCode);
        });

        mb.Entity<CostCategory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.Property(x => x.Name).HasMaxLength(80);
            b.HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Contract>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.HasOne(x => x.Project).WithMany(x => x.Contracts)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Party).WithMany(x => x.Contracts)
                .HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CostCategory).WithMany()
                .HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<ContractLine>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Contract).WithMany(x => x.Lines)
                .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostCategory).WithMany(x => x.ContractLines)
                .HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<Measurement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.HasOne(x => x.Project).WithMany(x => x.Measurements)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<MeasurementLine>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Measurement).WithMany(x => x.Lines)
                .HasForeignKey(x => x.MeasurementId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostCategory).WithMany(x => x.MeasurementLines)
                .HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<Payment>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.HasOne(x => x.Party).WithMany(x => x.Payments)
                .HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Contract).WithMany(x => x.Payments)
                .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Measurement).WithMany()
                .HasForeignKey(x => x.MeasurementId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<CostEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.SourceVoucherRef).HasMaxLength(48);
            b.HasIndex(x => new { x.ProjectId, x.CostCategoryId });
            b.HasOne(x => x.Project).WithMany(x => x.CostEntries)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostCategory).WithMany(x => x.CostEntries)
                .HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Contract).WithMany(x => x.CostEntries)
                .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Measurement).WithMany(x => x.CostEntries)
                .HasForeignKey(x => x.MeasurementId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.WbsItem).WithMany()
                .HasForeignKey(x => x.WbsItemId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---- 成本管理内核：部位(WBS) 与 工程量清单(BOQ) ----
        mb.Entity<WBSItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.FullPath).HasMaxLength(300);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<BoqLine>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32);
            b.Property(x => x.ItemName).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(16);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostCategory).WithMany().HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.WbsItem).WithMany().HasForeignKey(x => x.WbsItemId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<CostTarget>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CostCategory).WithMany().HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.ProjectId, x.CostCategoryId }).IsUnique();
        });

        // ---- M2-A 元数据层配置 ----
        mb.Entity<LookupDef>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(80);
            b.HasIndex(x => x.Code).IsUnique();
        });
        mb.Entity<LookupItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.Color).HasMaxLength(9);   // #RRGGBB 或 #RRGGBBAA
            b.HasOne(x => x.LookupDef).WithMany(x => x.Items)
                .HasForeignKey(x => x.LookupDefId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<SystemParam>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(64);
            b.Property(x => x.Value).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(200);
            b.HasIndex(x => x.Key).IsUnique();
        });
        mb.Entity<EntityDef>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(80);
            b.HasIndex(x => x.Code).IsUnique();
        });
        mb.Entity<FieldDef>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EntityDefCode).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.LookupDefCode).HasMaxLength(48);
            b.HasOne(x => x.EntityDef).WithMany(x => x.Fields)
                .HasForeignKey(x => x.EntityDefId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<CustomFieldValue>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EntityDefCode).HasMaxLength(48);
            b.Property(x => x.Value).HasMaxLength(512);
            b.HasIndex(x => new { x.EntityDefCode, x.OwnerId });
            b.HasOne(x => x.FieldDef).WithMany()
                .HasForeignKey(x => x.FieldDefId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- 阶段1：运行时自建对象 ----
        mb.Entity<DynamicRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EntityDefCode).HasMaxLength(48);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(120);
            b.HasIndex(x => x.EntityDefCode);
        });
        mb.Entity<DynamicValue>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Value).HasMaxLength(512);
            b.HasOne(x => x.Record).WithMany(x => x.Values)
                .HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FieldDef).WithMany()
                .HasForeignKey(x => x.FieldDefId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- M2-C 报表层配置 ----
        mb.Entity<ReportDef>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.Title).HasMaxLength(120);
            b.Property(x => x.DataSourceCode).HasMaxLength(48);
            b.HasIndex(x => x.Code).IsUnique();
        });
        mb.Entity<ReportColumn>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FieldKey).HasMaxLength(64);
            b.Property(x => x.Label).HasMaxLength(80);
            b.Property(x => x.Format).HasMaxLength(24);
            b.HasOne(x => x.ReportDef).WithMany(r => r.Columns)
                .HasForeignKey(x => x.ReportDefId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<ReportFilter>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FieldKey).HasMaxLength(64);
            b.Property(x => x.Value).HasMaxLength(200);
            b.HasOne(x => x.ReportDef).WithMany(r => r.Filters)
                .HasForeignKey(x => x.ReportDefId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- M2-D 看板层配置 ----
        mb.Entity<DashboardWidget>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.WidgetKey).HasMaxLength(48);
            b.Property(x => x.Title).HasMaxLength(80);
            b.HasIndex(x => x.WidgetKey).IsUnique();
        });

        // ---- M2-E 业务单据层配置 ----
        mb.Entity<ChangeOrder>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Title).HasMaxLength(120);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.CostCategory).WithMany().HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<MaterialReceipt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.MaterialName).HasMaxLength(120);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.CostCategory).WithMany().HasForeignKey(x => x.CostCategoryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.WbsItem).WithMany().HasForeignKey(x => x.WbsItemId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<Invoice>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.Property(x => x.Status).HasMaxLength(32);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<Settlement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(48);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---- M3-A 预警与调度层配置 ----
        mb.Entity<AlertRule>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.MetricKey).HasMaxLength(48);
        });
        mb.Entity<AlertRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.RuleName).HasMaxLength(80);
            b.Property(x => x.MetricKey).HasMaxLength(48);
            b.Property(x => x.Title).HasMaxLength(120);
            b.Property(x => x.Message).HasMaxLength(300);
            b.HasIndex(x => new { x.RuleId, x.ProjectId });
        });
        mb.Entity<ScheduledTask>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(80);
        });

        // 审计日志（只追加）
        mb.Entity<AuditLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EntityName).HasMaxLength(64);
            b.Property(x => x.By).HasMaxLength(64);
            b.HasIndex(x => new { x.EntityName, x.EntityId });
            b.HasIndex(x => x.At);
        });
    }
}

/// <summary>
/// 数据库引导：SQLite 连接 + 结构版本 + 安全升级（升级前自动备份，杜绝静默丢数据）+ 种子。
/// 结构版本存于 SQLite 的 PRAGMA user_version；每次改动模型（加字段/表）后把 SchemaVersion +1。
/// </summary>
public static class DbBootstrap
{
    /// <summary>当前库结构版本。每次变更模型后 +1，旧库会自动备份并重建。</summary>
    public const int SchemaVersion = 9;

    /// <summary>当前账套文件路径（Configure 时记录，供备份等使用）。</summary>
    public static string? CurrentDbPath { get; private set; }

    public static DbContextOptions<AppDbContext> Configure(string dbPath)
    {
        CurrentDbPath = dbPath;
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
    }

    public static void EnsureCreatedAndSeed(AppDbContext db, string dbPath)
    {
        bool existed = File.Exists(dbPath);
        int ver = existed ? ReadUserVersion(dbPath) : -1;

        if (!existed)
        {
            db.Database.EnsureCreated();
            SetUserVersion(dbPath, SchemaVersion);
        }
        else if (ver != SchemaVersion)
        {
            // 结构升级：先把旧库整份备份到 backups/，再重建，绝不静默丢数据
            var backup = BackupFile(dbPath, ver);
            Console.WriteLine($"[db] 结构升级 v{ver} → v{SchemaVersion}，旧库已备份：{backup}");
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            SetUserVersion(dbPath, SchemaVersion);
        }

        SeedData.SeedIfEmpty(db);
    }

    /// <summary>把账套备份到 backups/ 下（带版本与时间戳），并只保留最近 10 份。</summary>
    public static string BackupFile(string dbPath, int version)
    {
        var dir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "backups");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"costledger_v{version}_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(dbPath, target, overwrite: true);
        PruneBackups(dir, keep: 10);
        return target;
    }

    /// <summary>手动备份到用户指定文件。</summary>
    public static void BackupTo(string dbPath, string targetFile) => File.Copy(dbPath, targetFile, overwrite: true);

    private static void PruneBackups(string dir, int keep)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.db").OrderByDescending(f => f).ToList();
            foreach (var f in files.Skip(keep)) File.Delete(f);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[db] 清理旧备份失败：{ex.Message}");
        }
    }

    private static int ReadUserVersion(string path)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetUserVersion(string path, int version)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }
}
