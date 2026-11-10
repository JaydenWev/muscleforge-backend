using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Exercises;
using MuscleForge.Infrastructure.Identity;
using Npgsql;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class ExerciseSeedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RepeatedAndConcurrentSeedsPreserveEditsAndSeparateOwners()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var first = new ApplicationUser { Id = Guid.NewGuid(), UserName = "first" };
        var second = new ApplicationUser { Id = Guid.NewGuid(), UserName = "second" };
        context.Users.AddRange(first, second);
        context.Exercises.Add(new Exercise(first.Id, "Barbell Squat", "Custom"));
        await context.SaveChangesAsync(CancellationToken);

        var count = await new ExerciseCatalogSeeder(context).SeedAsync(first.Id, CancellationToken);
        Assert.True(count > 0);
        context.ChangeTracker.Clear();
        var original = await context.Exercises.SingleAsync(x => x.OwnerId == first.Id && x.SeedKey == "barbell-squat", CancellationToken);
        var originalId = original.Id;
        original.Update("My squat", "Rack", ExerciseLoadConvention.PerDumbbell);
        var timestamp = DateTimeOffset.UtcNow;
        original.Archive(timestamp);
        await context.SaveChangesAsync(CancellationToken);
        Assert.Equal(0, await new ExerciseCatalogSeeder(context).SeedAsync(first.Id, CancellationToken));

        async Task<int> SeedSecondAsync()
        {
            await using var concurrentContext = database.CreateContext();
            return await new ExerciseCatalogSeeder(concurrentContext).SeedAsync(second.Id, CancellationToken);
        }
        var concurrent = await Task.WhenAll(SeedSecondAsync(), SeedSecondAsync());
        Assert.Equal(count, concurrent.Sum());
        context.ChangeTracker.Clear();
        var entries = await context.Exercises.ToListAsync(CancellationToken);
        var edited = Assert.Single(entries, x => x.Id == originalId);
        Assert.Equal("My squat", edited.Name);
        Assert.Equal("Rack", edited.Equipment);
        Assert.Equal(ExerciseLoadConvention.PerDumbbell, edited.LoadConvention);
        Assert.NotNull(edited.ArchivedAt);
        Assert.Equal(timestamp.ToUnixTimeSeconds(), edited.ArchivedAt.Value.ToUnixTimeSeconds());
        Assert.Single(entries, x => x.OwnerId == first.Id && x.SeedKey == null && x.Name == "Barbell Squat");
        Assert.Equal(count, entries.Count(x => x.OwnerId == first.Id && x.SeedKey != null));
        Assert.Equal(count, entries.Count(x => x.OwnerId == second.Id && x.SeedKey != null));
        Assert.DoesNotContain(entries.Where(x => x.OwnerId == second.Id), x => x.Id == originalId);
    }

    [Fact]
    public async Task MissingOwnersAndDuplicateSeedKeysAreRejected()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var seeder = new ExerciseCatalogSeeder(context);
        await Assert.ThrowsAsync<ArgumentException>(() => seeder.SeedAsync(Guid.Empty, CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => seeder.SeedAsync(Guid.NewGuid(), CancellationToken));
        Assert.Empty(await context.Exercises.ToListAsync(CancellationToken));
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = "owner" };
        context.Users.Add(owner);
        await context.SaveChangesAsync(CancellationToken);
        await seeder.SeedAsync(owner.Id, CancellationToken);
        var existing = await context.Exercises.FirstAsync(CancellationToken);
        var duplicateId = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Exercises" ("Id", "OwnerId", "Name", "Equipment", "TrackingType", "LoadConvention", "SeedKey")
            VALUES ({duplicateId}, {owner.Id}, {"Duplicate"}, {"Barbell"}, {"ExternalWeightAndReps"}, {"TotalLoad"}, {existing.SeedKey})
            """, CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }
}
