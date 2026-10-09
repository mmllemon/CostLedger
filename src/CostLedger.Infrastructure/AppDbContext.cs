using CostLedger.Domain;
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
            b.HasIndex(x => x.Role);
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
        SeedData.SeedIfEmpty(db);
    }
}
