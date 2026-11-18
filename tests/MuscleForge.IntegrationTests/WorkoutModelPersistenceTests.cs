using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MuscleForge.Core.Exercises;
using MuscleForge.Core.Sessions;
using MuscleForge.Core.Templates;
using MuscleForge.Core.Workouts;
using Npgsql;
using MuscleForge.Infrastructure.Identity;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class WorkoutModelPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WorkoutMigrationPreservesExistingAccountsAndExercisesAcrossUpgradeAndDowngrade()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var predecessor = Assert.Single(migrations, name => name.EndsWith("_AccountPreferences", StringComparison.Ordinal));
        Assert.NotEqual(predecessor, migrations[^1]);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(predecessor, CancellationToken);
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "preserved",
            Email = "preserved@example.test",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        var exercise = new Exercise(owner.Id, "Preserved squat", "Barbell");
        context.Users.Add(owner);
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);
        context.ChangeTracker.Clear();

        await context.Database.MigrateAsync(CancellationToken);
        Assert.Equal(owner.Id, (await context.Users.SingleAsync(CancellationToken)).Id);
        Assert.Equal(exercise.Id, (await context.Exercises.SingleAsync(CancellationToken)).Id);
        await migrator.MigrateAsync(predecessor, CancellationToken);
        context.ChangeTracker.Clear();
        Assert.Equal("preserved@example.test", (await context.Users.SingleAsync(CancellationToken)).Email);
        var saved = await context.Exercises.SingleAsync(CancellationToken);
        Assert.Equal(exercise.Id, saved.Id);
        Assert.Equal("Preserved squat", saved.Name);
    }

    [Fact]
    public async Task AggregateRoundTripsClientIdsDraftValuesSnapshotsAndReceipt()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = NewUser();
        var exercise = new Exercise(owner.Id, "Squat", "Barbell");
        var template = new WorkoutTemplate(owner.Id, "Leg day");
        var templateExercise = template.AddExercise(exercise, 0, 90);
        template.AddSet(templateExercise.Id, 0, SetKind.Working, 42.125m, 8, 10);
        var templateSet = Assert.Single(templateExercise.Sets);
        var sessionId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var session = WorkoutSession.FromTemplate(sessionId, owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64), template,
            new Dictionary<Guid, Exercise> { [exercise.Id] = exercise },
            new Dictionary<Guid, Guid> { [templateExercise.Id] = exerciseId },
            new Dictionary<Guid, Guid> { [templateSet.Id] = setId });
        var receipt = new WorkoutMutationReceipt(owner.Id, Guid.NewGuid(), sessionId, new string('b', 64), 1, "{\"ok\":true}", DateTimeOffset.UtcNow);
        context.Users.Add(owner);
        context.Exercises.Add(exercise);
        context.Set<WorkoutTemplate>().Add(template);
        context.Set<WorkoutSession>().Add(session);
        context.Set<WorkoutMutationReceipt>().Add(receipt);
        await context.SaveChangesAsync(CancellationToken);
        exercise.Rename("Changed", "Dumbbell");
        template.Rename("Changed template");
        await context.SaveChangesAsync(CancellationToken);

        await using var reloaded = database.CreateContext();
        var saved = await reloaded.Set<WorkoutSession>().Include(x => x.Exercises).ThenInclude(x => x.Sets).SingleAsync(CancellationToken);
        Assert.Equal(sessionId, saved.Id);
        Assert.Equal("Leg day", saved.TemplateName);
        Assert.Equal(1, saved.SourceTemplateVersion);
        var savedExercise = Assert.Single(saved.Exercises);
        Assert.Equal(exerciseId, savedExercise.Id);
        Assert.Equal("Squat", savedExercise.ExerciseName);
        Assert.Equal("Barbell", savedExercise.Equipment);
        Assert.Equal(90, savedExercise.RestSeconds);
        var savedSet = Assert.Single(savedExercise.Sets);
        Assert.Equal(setId, savedSet.Id);
        Assert.Equal(42.125m, savedSet.TargetWeightKg);
        Assert.Equal(8, savedSet.TargetRepsMin);
        Assert.Equal(10, savedSet.TargetRepsMax);
        Assert.Equal(SetKind.Working, savedSet.TargetKind);
        Assert.Null(savedSet.WeightKg);
        Assert.Null(savedSet.Reps);
        Assert.False(savedSet.Completed);
        var savedReceipt = await reloaded.Set<WorkoutMutationReceipt>().SingleAsync(CancellationToken);
        Assert.Equal(receipt.MutationId, savedReceipt.MutationId);
        Assert.Equal(receipt.SemanticRequestHash, savedReceipt.SemanticRequestHash);
        Assert.Equal(receipt.ResultVersion, savedReceipt.ResultVersion);
        Assert.Equal(receipt.Acknowledgement, savedReceipt.Acknowledgement);
        var malformedReceipt = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            "UPDATE \"WorkoutMutationReceipts\" SET \"Acknowledgement\" = '[]'", CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, malformedReceipt.SqlState);
        var duplicateReceipt = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            INSERT INTO "WorkoutMutationReceipts" ("OwnerId", "MutationId", "SessionId", "SemanticRequestHash", "ResultVersion", "Acknowledgement", "CreatedAt")
            SELECT "OwnerId", "MutationId", "SessionId", "SemanticRequestHash", "ResultVersion", "Acknowledgement", "CreatedAt" FROM "WorkoutMutationReceipts"
            """, CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateReceipt.SqlState);
    }

    [Fact]
    public async Task StaleSessionContextCannotOverwriteAConcurrentMutation()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync(CancellationToken);
        var owner = NewUser();
        var session = new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64));
        setup.Users.Add(owner);
        setup.Set<WorkoutSession>().Add(session);
        await setup.SaveChangesAsync(CancellationToken);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.Set<WorkoutSession>().SingleAsync(CancellationToken);
        var staleCopy = await second.Set<WorkoutSession>().SingleAsync(CancellationToken);
        firstCopy.SetNotes("first"); firstCopy.AdvanceVersion();
        staleCopy.SetNotes("stale"); staleCopy.AdvanceVersion();
        await first.SaveChangesAsync(CancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(CancellationToken));
        await using var verify = database.CreateContext();
        var saved = await verify.Set<WorkoutSession>().SingleAsync(CancellationToken);
        Assert.Equal("first", saved.Notes);
        Assert.Equal(2, saved.Version);
    }


    [Theory]
    [InlineData("UPDATE \"WorkoutSets\" SET \"Completed\" = true", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"WeightKg\" = -1", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"WeightKg\" = 'NaN'::numeric", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"Reps\" = 0", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"TargetRepsMin\" = 10, \"TargetRepsMax\" = 5", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"SessionExercises\" SET \"RestSeconds\" = -1", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"Position\" = -1", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSets\" SET \"Kind\" = 'Invalid'", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSessions\" SET \"Status\" = 'Invalid'", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSessions\" SET \"Status\" = 'Completed', \"EndedAt\" = NULL", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSessions\" SET \"EndedAt\" = \"StartedAt\"", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSessions\" SET \"Version\" = 0", PostgresErrorCodes.CheckViolation)]
    [InlineData("UPDATE \"WorkoutSessions\" SET \"TemplateName\" = 'partial'", PostgresErrorCodes.CheckViolation)]
    public async Task DatabaseRejectsMalformedValuesEvenWhenDomainIsBypassed(string sql, string state)
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = NewUser();
        var exercise = new Exercise(owner.Id, "Squat", "Barbell");
        var session = new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64));
        var exerciseId = Guid.NewGuid();
        session.AddExercise(exerciseId, exercise, 0);
        session.AddSet(exerciseId, Guid.NewGuid(), 0, SetKind.Working);
        context.Users.Add(owner); context.Exercises.Add(exercise); context.Set<WorkoutSession>().Add(session);
        await context.SaveChangesAsync(CancellationToken);
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql, CancellationToken));
        Assert.Equal(state, error.SqlState);
    }

    [Fact]
    public async Task OneActiveSessionPerOwnerIsEnforcedUnderConcurrentInserts()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync(CancellationToken);
        var owner = NewUser(); setup.Users.Add(owner); await setup.SaveChangesAsync(CancellationToken);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        first.Set<WorkoutSession>().Add(new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64)));
        second.Set<WorkoutSession>().Add(new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('b', 64)));
        static async Task<Exception?> Save(MuscleForge.Infrastructure.Persistence.MuscleForgeDbContext context, CancellationToken token)
        {
            try { await context.SaveChangesAsync(token); return null; } catch (DbUpdateException ex) { return ex; }
        }
        var results = await Task.WhenAll(Save(first, CancellationToken), Save(second, CancellationToken));
        Assert.Single(results, result => result is null);
        var failure = Assert.IsType<DbUpdateException>(Assert.Single(results, result => result is not null));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        Assert.Equal(1, await setup.Set<WorkoutSession>().CountAsync(CancellationToken));
    }


    [Fact]
    public async Task CompositeForeignKeysAndParentOrderingRejectMalformedWrites()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = NewUser(); var other = NewUser();
        var exercise = new Exercise(owner.Id, "Squat", "Barbell");
        var foreignExercise = new Exercise(other.Id, "Press", "Barbell");
        var template = new WorkoutTemplate(owner.Id, "Template");
        var foreignTemplate = new WorkoutTemplate(other.Id, "Other");
        var templateExercise = template.AddExercise(exercise, 0);
        template.AddSet(templateExercise.Id, 0, SetKind.Working, null, null, null);
        var session = new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64), template.Id, template.Name, template.Version);
        var childId = Guid.NewGuid(); session.AddExercise(childId, exercise, 0);
        session.AddSet(childId, Guid.NewGuid(), 0, SetKind.Working);
        context.Users.AddRange(owner, other); context.Exercises.AddRange(exercise, foreignExercise);
        context.Set<WorkoutTemplate>().AddRange(template, foreignTemplate); context.Set<WorkoutSession>().Add(session);
        await context.SaveChangesAsync(CancellationToken);

        foreach (var sql in new[]
        {
            $"UPDATE \"TemplateExercises\" SET \"ExerciseId\" = '{foreignExercise.Id}'",
            $"UPDATE \"TemplateExercises\" SET \"OwnerId\" = '{other.Id}'",
            $"UPDATE \"TemplateSets\" SET \"OwnerId\" = '{other.Id}'",
            $"UPDATE \"WorkoutSessions\" SET \"TemplateId\" = '{foreignTemplate.Id}'",
            $"UPDATE \"SessionExercises\" SET \"ExerciseId\" = '{foreignExercise.Id}'",
            $"UPDATE \"SessionExercises\" SET \"OwnerId\" = '{other.Id}'",
            $"UPDATE \"WorkoutSets\" SET \"OwnerId\" = '{other.Id}'"
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql, CancellationToken));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync($"""
            INSERT INTO "WorkoutSets" ("Id", "OwnerId", "SessionExerciseId", "Position", "Kind", "TargetKind", "Completed")
            VALUES ('{Guid.NewGuid()}', '{owner.Id}', '{childId}', 0, 'Working', 'Working', false)
            """, CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Fact]
    public async Task FailedMultirowWriteRollsBackAggregateAndReceiptTogether()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = NewUser(); context.Users.Add(owner); await context.SaveChangesAsync(CancellationToken);
        var session = new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64));
        var receipt = new WorkoutMutationReceipt(owner.Id, Guid.NewGuid(), session.Id, new string('b', 64), 1, "{}", DateTimeOffset.UtcNow);
        await using (var transaction = await context.Database.BeginTransactionAsync(CancellationToken))
        {
            context.Set<WorkoutSession>().Add(session); context.Set<WorkoutMutationReceipt>().Add(receipt);
            await context.SaveChangesAsync(CancellationToken);
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE \"WorkoutSessions\" SET \"Version\" = 0", CancellationToken));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            await transaction.RollbackAsync(CancellationToken);
        }
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Set<WorkoutSession>().ToListAsync(CancellationToken));
        Assert.Empty(await verify.Set<WorkoutMutationReceipt>().ToListAsync(CancellationToken));
    }


    [Fact]
    public async Task PersistedSessionRequiresCompletedWorkingSetAndRejectsTerminalEdits()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = NewUser(); var exercise = new Exercise(owner.Id, "Squat", "Barbell");
        var session = new WorkoutSession(Guid.NewGuid(), owner.Id, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64));
        var childId = Guid.NewGuid(); var setId = Guid.NewGuid();
        session.AddExercise(childId, exercise, 0); session.AddSet(childId, setId, 0, SetKind.Working);
        context.Users.Add(owner); context.Exercises.Add(exercise); context.Set<WorkoutSession>().Add(session);
        await context.SaveChangesAsync(CancellationToken); context.ChangeTracker.Clear();
        var loaded = await context.Set<WorkoutSession>().Include(x => x.Exercises).ThenInclude(x => x.Sets).SingleAsync(CancellationToken);
        Assert.Throws<InvalidOperationException>(() => loaded.Complete(DateTimeOffset.UtcNow));
        loaded.UpdateSet(childId, setId, SetKind.Working, 12.125m, 5, true, null);
        loaded.AdvanceVersion(); loaded.Complete(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(CancellationToken); context.ChangeTracker.Clear();
        var terminal = await context.Set<WorkoutSession>().Include(x => x.Exercises).ThenInclude(x => x.Sets).SingleAsync(CancellationToken);
        Assert.Equal(WorkoutSessionStatus.Completed, terminal.Status);
        Assert.Throws<InvalidOperationException>(() => terminal.SetNotes("late"));
        Assert.Throws<InvalidOperationException>(() => terminal.UpdateSet(childId, setId, SetKind.Working, 13m, 5, true, null));
    }

    [Fact]
    public async Task WorkoutColumnsAndIndexesSupportPrecisionHistoryAndReceiptLookup()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext(); await context.Database.MigrateAsync(CancellationToken);
        var columns = await database.QueryStringsAsync("""
            SELECT table_name || ':' || column_name || ':' || numeric_precision || ':' || numeric_scale
            FROM information_schema.columns WHERE table_schema = 'public'
              AND table_name IN ('TemplateSets', 'WorkoutSets') AND data_type = 'numeric'
            """, CancellationToken);
        Assert.Contains("TemplateSets:TargetWeightKg:10:3", columns);
        Assert.Contains("WorkoutSets:TargetWeightKg:10:3", columns);
        Assert.Contains("WorkoutSets:WeightKg:10:3", columns);
        var indexes = await database.QueryStringsAsync("""
            SELECT indexdef FROM pg_indexes WHERE schemaname = 'public'
              AND tablename IN ('WorkoutSessions', 'WorkoutMutationReceipts')
            """, CancellationToken);
        Assert.Contains(indexes, x => x.Contains("OwnerId", StringComparison.Ordinal) && x.Contains("StartedAt", StringComparison.Ordinal));
        Assert.Contains(indexes, x => x.Contains("OwnerId", StringComparison.Ordinal) && x.Contains("MutationId", StringComparison.Ordinal));
        Assert.Contains(indexes, x => x.Contains("UNIQUE", StringComparison.Ordinal) && x.Contains("InProgress", StringComparison.Ordinal));
    }


    [Fact]
    public async Task StaleTemplateContextCannotOverwriteConcurrentEdit()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var setup = database.CreateContext(); await setup.Database.MigrateAsync(CancellationToken);
        var owner = NewUser(); setup.Users.Add(owner);
        setup.Set<WorkoutTemplate>().Add(new WorkoutTemplate(owner.Id, "Initial"));
        await setup.SaveChangesAsync(CancellationToken);
        await using var first = database.CreateContext(); await using var second = database.CreateContext();
        var firstCopy = await first.Set<WorkoutTemplate>().SingleAsync(CancellationToken);
        var staleCopy = await second.Set<WorkoutTemplate>().SingleAsync(CancellationToken);
        firstCopy.Rename("First"); firstCopy.AdvanceVersion();
        staleCopy.Rename("Stale"); staleCopy.AdvanceVersion();
        await first.SaveChangesAsync(CancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(CancellationToken));
        await using var verify = database.CreateContext();
        Assert.Equal("First", (await verify.Set<WorkoutTemplate>().SingleAsync(CancellationToken)).Name);
    }

    private static ApplicationUser NewUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = Guid.NewGuid().ToString(),
        SecurityStamp = Guid.NewGuid().ToString()
    };

}
