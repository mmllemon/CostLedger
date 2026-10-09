using CostLedger.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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

    // M2-C 报表层
    public DbSet<ReportDef> ReportDefs => Set<ReportDef>();
    public DbSet<ReportColumn> ReportColumns => Set<ReportColumn>();
    public DbSet<ReportFilter> ReportFilters => Set<ReportFilter>();

    // M2-D 看板层
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();

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
            b.HasOne(x => x.LookupDef).WithMany(x => x.Items)
                .HasForeignKey(x => x.LookupDefId).OnDelete(DeleteBehavior.Cascade);
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
    }
}

/// <summary>M1 引导：配置 SQLite 连接 + EnsureCreated + 种子</summary>
public static class DbBootstrap
{
    public static DbContextOptions<AppDbContext> Configure(string dbPath)
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    public static void EnsureCreatedAndSeed(AppDbContext db)
    {
        db.Database.EnsureCreated();
        try
        {
            SeedData.SeedIfEmpty(db);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // M1 旧库缺 M2-A 元数据表：重建数据库并重种子（演示数据确定可重建）
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            SeedData.SeedIfEmpty(db);
        }
    }
}
