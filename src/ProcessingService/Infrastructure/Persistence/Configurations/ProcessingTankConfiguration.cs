using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcessingService.Domain.Tanks;
using ProcessingService.Domain.Unloads;

namespace ProcessingService.Infrastructure.Persistence.Configurations;

public sealed class ProcessingTankConfiguration : IEntityTypeConfiguration<ProcessingTank>
{
    public void Configure(EntityTypeBuilder<ProcessingTank> builder)
    {
        builder.ToTable("processing_tanks");

        builder.HasKey(tank => tank.Id);

        builder.Property(tank => tank.Code)
            .HasMaxLength(ProcessingTank.MaxCodeLength)
            .IsRequired();

        builder.Property(tank => tank.Name)
            .HasMaxLength(ProcessingTank.MaxNameLength)
            .IsRequired();

        builder.Property(tank => tank.Kind).HasConversion<int>().IsRequired();
        builder.Property(tank => tank.Status).HasConversion<int>().IsRequired();
        builder.Property(tank => tank.CapacityLitres).HasPrecision(10, 2).IsRequired();

        builder.HasIndex(tank => tank.Code)
            .IsUnique()
            .HasDatabaseName("ux_processing_tanks_code");
    }
}

public sealed class UnloadConfiguration : IEntityTypeConfiguration<Unload>
{
    public void Configure(EntityTypeBuilder<Unload> builder)
    {
        builder.ToTable("unloads");

        builder.HasKey(unload => unload.Id);

        builder.Property(unload => unload.Reference)
            .HasMaxLength(Unload.MaxReferenceLength)
            .IsRequired();

        builder.Property(unload => unload.DispatchNoteReference)
            .HasMaxLength(Unload.MaxDispatchReferenceLength)
            .IsRequired();

        builder.Property(unload => unload.QuantityLitres).HasPrecision(10, 2).IsRequired();
        builder.Property(unload => unload.TemperatureCelsius).HasPrecision(5, 2).IsRequired();
        builder.Property(unload => unload.UnloadedBy).HasMaxLength(100);
        builder.Property(unload => unload.UnloadedAtLocal).IsRequired();
        builder.Property(unload => unload.UnloadDate).IsRequired();
        builder.Property(unload => unload.RecordedAtUtc).IsRequired();

        builder.HasOne(unload => unload.StoringTank)
            .WithMany()
            .HasForeignKey(unload => unload.StoringTankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(unload => unload.Reference)
            .IsUnique()
            .HasDatabaseName("ux_unloads_reference");

        // A bowser's load is unloaded once. The index is what settles a race between two officers
        // recording the same arrival; the service check only gives a better message.
        builder.HasIndex(unload => unload.DispatchNoteReference)
            .IsUnique()
            .HasDatabaseName("ux_unloads_dispatch_note");

        builder.HasIndex(unload => unload.UnloadDate)
            .HasDatabaseName("ix_unloads_date");
    }
}
