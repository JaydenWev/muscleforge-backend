using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;

namespace MuscleForge.Infrastructure.Persistence;

public sealed class MuscleForgeDbContext(DbContextOptions<MuscleForgeDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    // Match .NET's Unicode whitespace validation independently of the database locale.
    private const string NonWhitespacePattern =
        """U&'[^\0009-\000D\0020\0085\00A0\1680\2000-\200A\2028\2029\202F\205F\3000]'""";

    public DbSet<Exercise> Exercises => Set<Exercise>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Exercise>(exercise =>
        {
            exercise.ToTable("Exercises", table =>
            {
                table.HasCheckConstraint("CK_Exercises_Name_NotBlank", "\"Name\" ~ " + NonWhitespacePattern);
                table.HasCheckConstraint("CK_Exercises_Equipment_NotBlank", "\"Equipment\" ~ " + NonWhitespacePattern);
                table.HasCheckConstraint("CK_Exercises_SeedKey_NotBlank", "\"SeedKey\" IS NULL OR \"SeedKey\" ~ " + NonWhitespacePattern);
                table.HasCheckConstraint("CK_Exercises_LoadConvention_Supported",
                    "\"LoadConvention\" IN ('TotalLoad', 'PerDumbbell')");
                table.HasCheckConstraint("CK_Exercises_TrackingType_Supported",
                    "\"TrackingType\" = 'ExternalWeightAndReps'");
            });
            exercise.HasKey(x => x.Id);
            exercise.Property(x => x.Name).IsRequired().HasMaxLength(Exercise.MaximumNameLength);
            exercise.Property(x => x.Equipment).IsRequired().HasMaxLength(Exercise.MaximumEquipmentLength);
            exercise.Property(x => x.TrackingType).HasConversion<string>().IsRequired().HasMaxLength(32);
            exercise.Property(x => x.LoadConvention).HasConversion<string>().IsRequired().HasMaxLength(32)
                .HasDefaultValue(ExerciseLoadConvention.TotalLoad);
            exercise.Property(x => x.SeedKey).HasMaxLength(Exercise.MaximumSeedKeyLength);
            exercise.HasIndex(x => new { x.OwnerId, x.SeedKey }).IsUnique().HasFilter("\"SeedKey\" IS NOT NULL");
            exercise.HasIndex(x => new { x.OwnerId, x.Name });
            exercise.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
