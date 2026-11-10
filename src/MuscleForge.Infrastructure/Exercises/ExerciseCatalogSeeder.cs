using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Infrastructure.Exercises;

public sealed class ExerciseCatalogSeeder(MuscleForgeDbContext db)
{
    private static readonly (string Key, string Name, string Equipment, ExerciseLoadConvention Convention)[] Catalog =
    [
        ("barbell-squat", "Barbell Squat", "Barbell", ExerciseLoadConvention.TotalLoad),
        ("barbell-bench-press", "Barbell Bench Press", "Barbell", ExerciseLoadConvention.TotalLoad),
        ("barbell-deadlift", "Barbell Deadlift", "Barbell", ExerciseLoadConvention.TotalLoad),
        ("barbell-row", "Barbell Row", "Barbell", ExerciseLoadConvention.TotalLoad),
        ("barbell-overhead-press", "Barbell Overhead Press", "Barbell", ExerciseLoadConvention.TotalLoad),
        ("dumbbell-curl", "Dumbbell Curl", "Dumbbells", ExerciseLoadConvention.PerDumbbell),
        ("dumbbell-press", "Dumbbell Press", "Dumbbells", ExerciseLoadConvention.PerDumbbell),
        ("dumbbell-row", "Dumbbell Row", "Dumbbells", ExerciseLoadConvention.PerDumbbell),
        ("lat-pulldown", "Lat Pulldown", "Cable machine", ExerciseLoadConvention.TotalLoad),
        ("leg-press", "Leg Press", "Leg press machine", ExerciseLoadConvention.TotalLoad),
        ("cable-triceps-pushdown", "Cable Triceps Pushdown", "Cable machine", ExerciseLoadConvention.TotalLoad)
    ];

    public async Task<int> SeedAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An owner is required.", nameof(ownerId));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Users.AnyAsync(user => user.Id == ownerId, cancellationToken))
        {
            throw new ArgumentException("The owner does not exist.", nameof(ownerId));
        }

        var inserted = 0;
        foreach (var item in Catalog)
        {
            var id = Guid.NewGuid();
            var convention = item.Convention.ToString();
            inserted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Exercises" ("Id", "OwnerId", "Name", "Equipment", "TrackingType", "LoadConvention", "SeedKey", "ArchivedAt")
                VALUES ({id}, {ownerId}, {item.Name}, {item.Equipment}, {"ExternalWeightAndReps"}, {convention}, {item.Key}, NULL)
                ON CONFLICT ("OwnerId", "SeedKey") WHERE "SeedKey" IS NOT NULL DO NOTHING
                """, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }
}
