using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;

namespace MuscleForge.Infrastructure.Persistence;

public sealed class MuscleForgeDbContext(DbContextOptions<MuscleForgeDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Exercise> Exercises => Set<Exercise>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Exercise>(exercise =>
        {
            exercise.ToTable("Exercises");
            exercise.HasKey(x => x.Id);
            exercise.Property(x => x.Name).IsRequired().HasMaxLength(Exercise.MaximumNameLength);
            exercise.Property(x => x.Equipment).IsRequired().HasMaxLength(Exercise.MaximumEquipmentLength);
            exercise.HasIndex(x => new { x.OwnerId, x.Name });
            exercise.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
