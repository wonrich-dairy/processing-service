using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcessingService.Domain.Runs;

namespace ProcessingService.Infrastructure.Persistence.Configurations;

public sealed class ProcessingRunConfiguration : IEntityTypeConfiguration<ProcessingRun>
{
    public void Configure(EntityTypeBuilder<ProcessingRun> builder)
    {
        builder.ToTable("processing_runs");

        builder.HasKey(run => run.Id);

        builder.Property(run => run.State).HasConversion<int>().IsRequired();
        builder.Property(run => run.StateChangedAtUtc).IsRequired();

        builder.HasOne(run => run.Unload)
            .WithOne()
            .HasForeignKey<ProcessingRun>(run => run.UnloadId)
            .OnDelete(DeleteBehavior.Restrict);

        // One unload produces exactly one run. The index is what settles a race between two
        // recordings against the same unload; the service flow only ever creates the one.
        builder.HasIndex(run => run.UnloadId)
            .IsUnique()
            .HasDatabaseName("ux_processing_runs_unload_id");
    }
}