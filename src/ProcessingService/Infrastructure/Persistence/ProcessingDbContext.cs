using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// EF Core context for Processing Service (SCRUM-57: full schema with precision, UTC datetime(6), varchar enums, concurrency token).
/// Includes MccDispatchTrace for true isolate + ProcessingRunStoringAllocation for partial unload split across tanks while keeping DispatchNumber UNIQUE.
/// </summary>
public class ProcessingDbContext : DbContext
{
    public ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : base(options)
    {
    }

    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();
    public DbSet<QualityPanel> QualityPanels => Set<QualityPanel>();
    public DbSet<TankAllocation> TankAllocations => Set<TankAllocation>();
    public DbSet<ProcessingStage> ProcessingStages => Set<ProcessingStage>();
    public DbSet<MccDispatchTrace> MccDispatchTraces => Set<MccDispatchTrace>();
    public DbSet<ProcessingRunStoringAllocation> ProcessingRunStoringAllocations => Set<ProcessingRunStoringAllocation>();
    public DbSet<TankTemperatureLog> TankTemperatureLogs => Set<TankTemperatureLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MccDispatchTrace - cached copy from mccdb for true isolate, auto-created via polling
        modelBuilder.Entity<MccDispatchTrace>(trace =>
        {
            trace.ToTable("mcc_dispatch_traces");
            trace.HasKey(t => t.Id);
            trace.Property(t => t.Reference).HasMaxLength(50).IsRequired();
            trace.HasIndex(t => t.Reference).IsUnique().HasDatabaseName("ux_mcc_dispatch_traces_reference");
            trace.Property(t => t.BowserRegistration).HasMaxLength(50).IsRequired();
            trace.Property(t => t.TotalQuantityLitres).HasPrecision(10, 2).IsRequired();
            trace.Property(t => t.DispatchedBy).HasMaxLength(100).IsRequired();
            trace.Property(t => t.RecordedAtUtc).HasColumnType("datetime(6)").IsRequired();
            trace.Property(t => t.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            trace.Property(t => t.LastSyncedAtUtc).HasColumnType("datetime(6)").IsRequired();
            trace.Property(t => t.TotalUnloadedKg).HasPrecision(10, 2).IsRequired();
        });

        // ProcessingRunStoringAllocation - allows one dispatch split across multiple tanks while keeping DispatchNumber UNIQUE
        modelBuilder.Entity<ProcessingRunStoringAllocation>(alloc =>
        {
            alloc.ToTable("processing_run_storing_allocations");
            alloc.HasKey(a => a.Id);
            alloc.Property(a => a.QuantityKg).HasPrecision(10, 2).IsRequired();
            alloc.Property(a => a.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            alloc.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
            alloc.HasOne(a => a.ProcessingRun).WithMany(r => r.StoringAllocations).HasForeignKey(a => a.ProcessingRunId).OnDelete(DeleteBehavior.Cascade);
            alloc.HasOne(a => a.StoringTank).WithMany().HasForeignKey(a => a.StoringTankId).OnDelete(DeleteBehavior.Restrict);
            alloc.HasIndex(a => new { a.ProcessingRunId, a.StoringTankId }).HasDatabaseName("ix_storing_alloc_run_tank");
        });

        // TankTemperatureLog - similar to MCC tanks, log temperature when needed with note
        modelBuilder.Entity<TankTemperatureLog>(log =>
        {
            log.ToTable("tank_temperature_logs");
            log.HasKey(l => l.Id);
            log.Property(l => l.TemperatureC).HasPrecision(10, 2).IsRequired();
            log.Property(l => l.Note).HasMaxLength(500);
            log.Property(l => l.RecordedAtUtc).HasColumnType("datetime(6)").IsRequired();
            log.Property(l => l.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            log.Property(l => l.RecordedBy).HasMaxLength(100).IsRequired();
            log.HasOne(l => l.Tank).WithMany().HasForeignKey(l => l.TankId).OnDelete(DeleteBehavior.Cascade);
            log.HasIndex(l => new { l.TankId, l.RecordedAtUtc }).HasDatabaseName("ix_temp_logs_tank_time");
        });

        // Tank
        modelBuilder.Entity<Tank>(tank =>
        {
            tank.ToTable("tanks");
            tank.HasKey(t => t.Id);
            tank.Property(t => t.Code).HasMaxLength(20).IsRequired();
            tank.HasIndex(t => t.Code).IsUnique().HasDatabaseName("ux_tanks_code");
            tank.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            tank.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            tank.Property(t => t.CapacityKg).HasPrecision(10, 2).IsRequired();
            tank.Property(t => t.RemainingKg).HasPrecision(10, 2).IsRequired();
            tank.Property(t => t.RowVersion).IsRowVersion().IsConcurrencyToken();
            tank.Property(t => t.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            tank.Property(t => t.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            tank.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
            tank.Property(t => t.UpdatedBy).HasMaxLength(100).IsRequired();

            tank.HasData(
                new Tank { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Code = "ST-01", Kind = TankKind.Storing, CapacityKg = 5000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" },
                new Tank { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Code = "ST-02", Kind = TankKind.Storing, CapacityKg = 5000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" },
                new Tank { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Code = "ST-03", Kind = TankKind.Storing, CapacityKg = 5000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" },
                new Tank { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Code = "MT-01", Kind = TankKind.Mixing, CapacityKg = 3000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" },
                new Tank { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Code = "MT-02", Kind = TankKind.Mixing, CapacityKg = 3000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" },
                new Tank { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Code = "MT-03", Kind = TankKind.Mixing, CapacityKg = 3000, RemainingKg = 0, Status = TankStatus.Active, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "seed", UpdatedBy = "seed" }
            );
        });

        // ProcessingRun - DispatchNumber remains UNIQUE, split handled via StoringAllocations child table
        modelBuilder.Entity<ProcessingRun>(run =>
        {
            run.ToTable("processing_runs");
            run.HasKey(r => r.Id);
            run.Property(r => r.DispatchNumber).HasMaxLength(50).IsRequired();
            run.HasIndex(r => r.DispatchNumber).IsUnique().HasDatabaseName("ux_processing_runs_dispatch");
            run.Property(r => r.QuantityKg).HasPrecision(10, 2).IsRequired();
            run.Property(r => r.TemperatureC).HasPrecision(10, 2).IsRequired();
            run.Property(r => r.State).HasConversion<string>().HasMaxLength(50).IsRequired();
            run.Property(r => r.QualityTestStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            run.Property(r => r.BatchCode).HasMaxLength(20);
            run.HasIndex(r => r.BatchCode).IsUnique().HasDatabaseName("ux_processing_runs_batchcode");
            run.Property(r => r.HoldReason).HasMaxLength(500);
            run.Property(r => r.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            run.Property(r => r.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            run.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
            run.HasOne(r => r.StoringTank).WithMany(t => t.ProcessingRuns).HasForeignKey(r => r.StoringTankId).OnDelete(DeleteBehavior.Restrict);
        });

        // QualityPanel
        modelBuilder.Entity<QualityPanel>(panel =>
        {
            panel.ToTable("quality_panels");
            panel.HasKey(p => p.Id);
            panel.Property(p => p.DispatchNumber).HasMaxLength(50).IsRequired();
            panel.Property(p => p.FatPercent).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.RawLactometerReading).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.TemperatureCelsius).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.WaterPercent).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.Snf).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.Ts).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.Ph).HasPrecision(10, 2).IsRequired();
            panel.Property(p => p.KqColour).HasMaxLength(20).IsRequired().HasConversion<string>();
            panel.Property(p => p.AlcoholOutcomesJson).HasMaxLength(1000).IsRequired();
            panel.Property(p => p.AlcoholResult).HasMaxLength(50).IsRequired();
            panel.Property(p => p.Verdict).HasMaxLength(20).IsRequired().HasConversion<string>();
            panel.Property(p => p.FailedParameter).HasMaxLength(100);
            panel.Property(p => p.FailedValue).HasMaxLength(100);
            panel.Property(p => p.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            panel.Property(p => p.ConfirmedAtUtc).HasColumnType("datetime(6)");
            panel.Property(p => p.ConfirmedBy).HasMaxLength(100);
            panel.HasOne(p => p.ProcessingRun).WithOne(r => r.QualityPanel).HasForeignKey<QualityPanel>(p => p.ProcessingRunId).OnDelete(DeleteBehavior.Cascade);
            panel.HasIndex(p => p.ProcessingRunId).IsUnique();
        });

        // TankAllocation
        modelBuilder.Entity<TankAllocation>(alloc =>
        {
            alloc.ToTable("tank_allocations");
            alloc.HasKey(a => a.Id);
            alloc.Property(a => a.QuantityKg).HasPrecision(10, 2).IsRequired();
            alloc.Property(a => a.ProductType).HasConversion<string>().HasMaxLength(20).IsRequired();
            alloc.Property(a => a.BatchNumber).IsRequired();
            alloc.Property(a => a.BatchLetter).HasMaxLength(2).IsRequired();
            alloc.Property(a => a.BatchCode).HasMaxLength(20).IsRequired();
            alloc.HasIndex(a => a.BatchCode).IsUnique().HasDatabaseName("ux_tank_allocations_batchcode");
            alloc.Property(a => a.AllocatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            alloc.Property(a => a.OverrideReason).HasMaxLength(500);
            alloc.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
            alloc.Property(a => a.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            alloc.HasOne(a => a.ProcessingRun).WithMany(r => r.Allocations).HasForeignKey(a => a.ProcessingRunId).OnDelete(DeleteBehavior.Cascade);
            alloc.HasOne(a => a.SourceStoringTank).WithMany(t => t.SourceAllocations).HasForeignKey(a => a.SourceStoringTankId).OnDelete(DeleteBehavior.Restrict);
            alloc.HasOne(a => a.DestinationMixingTank).WithMany(t => t.DestinationAllocations).HasForeignKey(a => a.DestinationMixingTankId).OnDelete(DeleteBehavior.Restrict);
            alloc.HasIndex(a => new { a.BatchNumber, a.ProductType, a.BatchLetter }).IsUnique().HasDatabaseName("ux_tank_allocations_day_product_letter");
        });

        // ProcessingStage
        modelBuilder.Entity<ProcessingStage>(stage =>
        {
            stage.ToTable("processing_stages");
            stage.HasKey(s => s.Id);
            stage.Property(s => s.StageType).HasConversion<string>().HasMaxLength(20).IsRequired();
            stage.Property(s => s.StartTimeUtc).HasColumnType("datetime(6)").IsRequired();
            stage.Property(s => s.EndTimeUtc).HasColumnType("datetime(6)");
            stage.Property(s => s.EndTemperatureC).HasPrecision(10, 2).IsRequired();
            stage.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
            stage.Property(s => s.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            stage.Property(s => s.CultureAddedAtUtc).HasColumnType("datetime(6)");
            stage.HasOne(s => s.ProcessingRun).WithMany(r => r.Stages).HasForeignKey(s => s.ProcessingRunId).OnDelete(DeleteBehavior.Cascade);
            stage.HasOne(s => s.MixingTank).WithMany(t => t.Stages).HasForeignKey(s => s.MixingTankId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProcessingDbContext).Assembly);
    }
}
