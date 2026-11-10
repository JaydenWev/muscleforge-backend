using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class PostgresMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CatalogMigrationPreservesExistingCustomExerciseAcrossUpgradeAndDowngrade()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[0], CancellationToken);
        var owner = CreateUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync(CancellationToken);
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Exercises" ("Id", "OwnerId", "Name", "Equipment", "TrackingType")
            VALUES ({id}, {owner.Id}, {"Barbell Squat"}, {"Custom rack"}, {"ExternalWeightAndReps"})
            """, CancellationToken);
        await context.Database.MigrateAsync(CancellationToken);
        var saved = await context.Exercises.SingleAsync(CancellationToken);
        Assert.Equal(id, saved.Id);
        Assert.Equal("Custom rack", saved.Equipment);
        Assert.Null(saved.SeedKey);
        Assert.Equal(ExerciseLoadConvention.TotalLoad, saved.LoadConvention);
        await migrator.MigrateAsync(migrations[0], CancellationToken);
        var names = await database.QueryStringsAsync("SELECT \"Name\" FROM \"Exercises\"", CancellationToken);
        Assert.Equal(["Barbell Squat"], names);
    }

    [Fact]
    public async Task StartupAndHealthRequestsLeaveAnEmptyDatabaseUnmigrated()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        Assert.False(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));

        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        using var status = await client.GetAsync("/api/v1/status", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Empty(await GetPublicTablesAsync(database));

        using var ready = await client.GetAsync("/health/ready", CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain("Host=", await ready.Content.ReadAsStringAsync(CancellationToken));
        using var live = await client.GetAsync("/health/live", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Empty(await GetPublicTablesAsync(database));
    }

    [Fact]
    public async Task ExplicitMigrationCanBeAppliedTwiceAndMakesTheDatabaseReady()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        var expectedMigrations = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(expectedMigrations);

        await context.Database.MigrateAsync(CancellationToken);
        var owner = CreateUser();
        context.Users.Add(owner);
        context.Exercises.Add(new Exercise(owner.Id, "Squat", "Barbell"));
        await context.SaveChangesAsync(CancellationToken);
        await context.Database.MigrateAsync(CancellationToken);

        Assert.Equal(expectedMigrations, await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(1, await context.Exercises.CountAsync(CancellationToken));
        Assert.True(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
        await AssertHttpReadinessAsync(database, HttpStatusCode.OK);
    }

    [Fact]
    public async Task IdempotentDeploymentScriptCreatesTheSchemaAndPreservesDataOnReapplication()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await database.ExecuteAsync(script, CancellationToken);
        var user = CreateUser();
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken);

        await database.ExecuteAsync(script, CancellationToken);

        context.ChangeTracker.Clear();
        Assert.Equal(user.Id, (await context.Users.SingleAsync(CancellationToken)).Id);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.True(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
    }

    [Fact]
    public async Task RollingBackToAnOlderSchemaMakesReadinessFailUntilExplicitUpgrade()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        // The initial release has one migration: its predecessor is migration 0.
        await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, CancellationToken);
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync(CancellationToken));
        Assert.False(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
        await AssertHttpReadinessAsync(database, HttpStatusCode.ServiceUnavailable);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(CancellationToken));

        await context.Database.MigrateAsync(CancellationToken);

        Assert.True(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
        await AssertHttpReadinessAsync(database, HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownMigrationHistoryIsRejectedWithoutChangingTheDatabase()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        await database.ExecuteAsync("""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('99991231235959_UnknownSchema', '10.0.14');
            """, CancellationToken);
        var applied = (await context.Database.GetAppliedMigrationsAsync(CancellationToken)).ToArray();

        Assert.False(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
        await AssertHttpReadinessAsync(database, HttpStatusCode.ServiceUnavailable);

        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.True(await context.Database.CanConnectAsync(CancellationToken));
    }

    [Fact]
    public async Task ReadinessRejectsAnUnavailableDatabase()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        var unavailable = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"missing_{Guid.NewGuid():N}",
            Timeout = 1
        };
        await using var context = new MuscleForgeDbContext(
            new DbContextOptionsBuilder<MuscleForgeDbContext>().UseNpgsql(unavailable.ConnectionString).Options);

        Assert.False(await new PostgresDatabaseProbe(context).IsReadyAsync(CancellationToken));
    }

    [Fact]
    public async Task MigrationCreatesIdentityTablesAndExpectedPostgresExerciseColumnTypes()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var tables = await GetPublicTablesAsync(database);
        foreach (var table in new[]
        {
            "AspNetUsers", "AspNetRoles", "AspNetRoleClaims", "AspNetUserClaims",
            "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens", "Exercises", "__EFMigrationsHistory"
        })
        {
            Assert.Contains(table, tables);
        }

        var columns = await database.QueryStringsAsync("""
            SELECT column_name || ':' || data_type || ':' || is_nullable || ':' ||
                COALESCE(character_maximum_length::text, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Exercises';
            """, CancellationToken);
        Assert.Contains("Id:uuid:NO:", columns);
        Assert.Contains("OwnerId:uuid:NO:", columns);
        Assert.Contains("Name:character varying:NO:100", columns);
        Assert.Contains("Equipment:character varying:NO:100", columns);
        Assert.Contains("TrackingType:character varying:NO:32", columns);
        Assert.Contains("LoadConvention:character varying:NO:32", columns);
        Assert.Contains("SeedKey:character varying:YES:64", columns);
        Assert.Contains("ArchivedAt:timestamp with time zone:YES:", columns);
        var indexes = await database.QueryStringsAsync("""
            SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'Exercises';
            """, CancellationToken);
        Assert.Contains(indexes, index => index.Contains("(\"OwnerId\", \"Name\")", StringComparison.Ordinal));
        var foreignKeys = await database.QueryStringsAsync("""
            SELECT source.relname || ':' || target.relname || ':' || constraint_info.confdeltype::text
            FROM pg_constraint AS constraint_info
            JOIN pg_class AS source ON source.oid = constraint_info.conrelid
            JOIN pg_class AS target ON target.oid = constraint_info.confrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = source.relnamespace
            WHERE constraint_info.contype = 'f' AND table_namespace.nspname = 'public';
            """, CancellationToken);
        foreach (var foreignKey in new[]
        {
            "AspNetRoleClaims:AspNetRoles:c", "AspNetUserClaims:AspNetUsers:c",
            "AspNetUserLogins:AspNetUsers:c", "AspNetUserRoles:AspNetUsers:c",
            "AspNetUserRoles:AspNetRoles:c", "AspNetUserTokens:AspNetUsers:c", "Exercises:AspNetUsers:r"
        })
        {
            Assert.Contains(foreignKey, foreignKeys);
        }
    }

    [Fact]
    public async Task IdentityAndArchivedExerciseRoundTripThroughPostgres()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var user = CreateUser();
        var hasher = new PasswordHasher<ApplicationUser>();
        user.PasswordHash = hasher.HashPassword(user, "An isolated test password 42!");
        var role = new IdentityRole<Guid>("Owner") { Id = Guid.NewGuid(), NormalizedName = "OWNER" };
        var exercise = new Exercise(user.Id, new string('N', 100), new string('E', 100));
        var archivedAt = new DateTimeOffset(2026, 11, 3, 12, 34, 56, TimeSpan.FromHours(1));
        exercise.Archive(archivedAt);
        context.Users.Add(user);
        context.Roles.Add(role);
        context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        context.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = user.Id, ClaimType = "test", ClaimValue = "owner" });
        context.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = "scope", ClaimValue = "personal" });
        context.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            UserId = user.Id, LoginProvider = "test", ProviderKey = "key", ProviderDisplayName = "Test provider"
        });
        context.UserTokens.Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id, LoginProvider = "test", Name = "test-token", Value = "synthetic test value"
        });
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        await using var reloaded = database.CreateContext();
        var savedUser = await reloaded.Users.SingleAsync(CancellationToken);
        Assert.Equal(user.Id, savedUser.Id);
        Assert.Equal(user.NormalizedEmail, savedUser.NormalizedEmail);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(savedUser, savedUser.PasswordHash!, "An isolated test password 42!"));
        Assert.Equal(user.Id, (await reloaded.UserRoles.SingleAsync(CancellationToken)).UserId);
        Assert.Equal(role.Id, (await reloaded.UserRoles.SingleAsync(CancellationToken)).RoleId);
        Assert.Equal("owner", (await reloaded.UserClaims.SingleAsync(CancellationToken)).ClaimValue);
        Assert.Equal("personal", (await reloaded.RoleClaims.SingleAsync(CancellationToken)).ClaimValue);
        Assert.Equal("key", (await reloaded.UserLogins.SingleAsync(CancellationToken)).ProviderKey);
        Assert.Equal("synthetic test value", (await reloaded.UserTokens.SingleAsync(CancellationToken)).Value);
        var savedExercise = await reloaded.Exercises.SingleAsync(CancellationToken);
        Assert.Equal(exercise.Id, savedExercise.Id);
        Assert.Equal(user.Id, savedExercise.OwnerId);
        Assert.Equal(exercise.Name, savedExercise.Name);
        Assert.Equal(exercise.Equipment, savedExercise.Equipment);
        Assert.Equal(ExerciseTrackingType.ExternalWeightAndReps, savedExercise.TrackingType);
        Assert.Equal(archivedAt.ToUniversalTime(), savedExercise.ArchivedAt);
        Assert.Equal(TimeSpan.Zero, savedExercise.ArchivedAt!.Value.Offset);
    }

    [Theory]
    [InlineData("Name", null, PostgresErrorCodes.NotNullViolation)]
    [InlineData("Name", "", PostgresErrorCodes.CheckViolation)]
    [InlineData("Name", "   ", PostgresErrorCodes.CheckViolation)]
    [InlineData("Name", "\t\r\n", PostgresErrorCodes.CheckViolation)]
    [InlineData("Name", "\u00a0\u2003\u202f\u3000", PostgresErrorCodes.CheckViolation)]
    [InlineData("Equipment", null, PostgresErrorCodes.NotNullViolation)]
    [InlineData("Equipment", "", PostgresErrorCodes.CheckViolation)]
    [InlineData("Equipment", "   ", PostgresErrorCodes.CheckViolation)]
    [InlineData("Equipment", "\t\r\n", PostgresErrorCodes.CheckViolation)]
    [InlineData("Equipment", "\u00a0\u2003\u202f\u3000", PostgresErrorCodes.CheckViolation)]
    [InlineData("TrackingType", "Timed", PostgresErrorCodes.CheckViolation)]
    [InlineData("TrackingType", null, PostgresErrorCodes.NotNullViolation)]
    public async Task DatabaseRejectsInvalidExerciseValuesEvenWhenBypassingTheDomain(
        string column, string? value, string expectedSqlState)
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = CreateUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync(CancellationToken);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertExerciseAsync(
            database, Guid.NewGuid(), owner.Id, column == "Name" ? value : "Squat",
            column == "Equipment" ? value : "Barbell",
            column == "TrackingType" ? value : nameof(ExerciseTrackingType.ExternalWeightAndReps)));

        Assert.Equal(expectedSqlState, exception.SqlState);
        Assert.Empty(await context.Exercises.ToListAsync(CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatabaseEnforcesTheExerciseTextLengthLimits(bool nameTooLong)
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var owner = CreateUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync(CancellationToken);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertExerciseAsync(
            database, Guid.NewGuid(), owner.Id, nameTooLong ? new string('x', 101) : "Squat",
            nameTooLong ? "Barbell" : new string('x', 101)));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, exception.SqlState);
    }

    [Fact]
    public async Task ExerciseForeignKeyRequiresAnOwnerAndPreventsDeletingAReferencedOwner()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(CancellationToken);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExerciseAsync(database, Guid.NewGuid(), Guid.NewGuid(), "Squat", "Barbell"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);

        var owner = CreateUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync(CancellationToken);
        var exerciseId = Guid.NewGuid();
        await InsertExerciseAsync(database, exerciseId, owner.Id, "Squat", "Barbell");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExerciseAsync(database, exerciseId, owner.Id, "Squat", "Barbell"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        context.Users.Remove(owner);
        var deletion = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(deletion.InnerException).SqlState);
        Assert.Equal(1, await context.Exercises.CountAsync(CancellationToken));
        Assert.Equal(1, await context.Users.CountAsync(CancellationToken));
    }

    private static ApplicationUser CreateUser() => new()
    {
        Id = Guid.NewGuid(), UserName = "owner@example.test", NormalizedUserName = "OWNER@EXAMPLE.TEST",
        Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", SecurityStamp = Guid.NewGuid().ToString()
    };

    private static Task<string[]> GetPublicTablesAsync(TestDatabase database) => database.QueryStringsAsync(
        "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;", CancellationToken);

    private static async Task AssertHttpReadinessAsync(TestDatabase database, HttpStatusCode expected)
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync("/health/ready", CancellationToken);
        Assert.Equal(expected, ready.StatusCode);
        using var live = await client.GetAsync("/health/live", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static async Task InsertExerciseAsync(TestDatabase database, Guid id, Guid ownerId,
        string? name, string? equipment, string? trackingType = nameof(ExerciseTrackingType.ExternalWeightAndReps))
    {
        await using var connection = await database.OpenConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO "Exercises" ("Id", "OwnerId", "Name", "Equipment", "TrackingType")
            VALUES (@id, @ownerId, @name, @equipment, @trackingType);
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("ownerId", ownerId);
        command.Parameters.AddWithValue("name", NpgsqlTypes.NpgsqlDbType.Text, (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("equipment", NpgsqlTypes.NpgsqlDbType.Text, (object?)equipment ?? DBNull.Value);
        command.Parameters.AddWithValue("trackingType", NpgsqlTypes.NpgsqlDbType.Text, (object?)trackingType ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
