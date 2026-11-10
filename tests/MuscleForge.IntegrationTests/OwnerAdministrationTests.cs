using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class OwnerAdministrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SecurityStampResetPreservesTheBootstrappedPasswordAndPersonalData()
    {
        await using var database = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await OwnerBootstrapTests.BootstrapOwnerAsync(database);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        var exercise = new Exercise(before.Id, "Personal press", "Dumbbells");
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", OwnerBootstrapTests.Email.ToUpperInvariant()]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("security stamp reset", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access tokens expire normally", result.Output, StringComparison.OrdinalIgnoreCase);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.False(string.IsNullOrEmpty(after.SecurityStamp));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(exercise.Id, (await context.Exercises.SingleAsync(x => x.Id == exercise.Id, CancellationToken)).Id);
        Assert.True(await OwnerBootstrapTests.CheckPasswordAsync(database, after, OwnerBootstrapTests.Password));
        Assert.False(result.Output.Contains(OwnerBootstrapTests.Password, StringComparison.Ordinal),
            "Administrative output must not reveal credentials.");
    }

    [Fact]
    public async Task ResettingAMissingAccountReportsNotFoundWithoutCreatingAnything()
    {
        await using var database = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", OwnerBootstrapTests.Email]);

        Assert.Equal(3, result.ExitCode);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task ResetRefusesAnUnmigratedDatabaseWithoutCreatingTheSchema()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", OwnerBootstrapTests.Email]);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(await database.QueryStringsAsync(
            "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public'", CancellationToken));
    }
}

public sealed class OwnerAdministrationUsageTests
{
    [Fact]
    public async Task HelpWorksWithoutDatabaseConfigurationAndAdvertisesOnlyAccountMaintenance()
    {
        var result = await OwnerCommandProcess.RunAsync(null, ["owner", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("reset-security-stamp", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("owner create", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--password-stdin", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner", "unknown")]
    [InlineData("owner", "create", "--email", "owner@example.test")]
    [InlineData("owner", "reset-security-stamp")]
    [InlineData("owner", "reset-security-stamp", "--email")]
    [InlineData("owner", "reset-security-stamp", "--email", "owner@example.test", "--email", "another@example.test")]
    [InlineData("owner", "reset-security-stamp", "--email", "owner@example.test", "--password-stdin")]
    [InlineData("owner", "reset-security-stamp", "--email", "owner@example.test", "--password", "Synthetic-ArgSecret!2468")]
    [InlineData("owner", "reset-security-stamp", "--email", "owner@example.test", "--ConnectionStrings:Database", "Synthetic-ArgSecret!2468")]
    public async Task MalformedArgumentsAreRejectedBeforeConfigurationAndAreNeverEchoed(params string[] arguments)
    {
        var result = await OwnerCommandProcess.RunAsync(null, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Output.Contains("Synthetic-ArgSecret!2468", StringComparison.Ordinal),
            "Unknown argument values must never be echoed.");
    }

    [Fact]
    public async Task ResetWithoutDatabaseConfigurationReturnsAnOperationalFailure()
    {
        var result = await OwnerCommandProcess.RunAsync(null,
            ["owner", "reset-security-stamp", "--email", "owner@example.test"]);

        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task DatabaseConnectionFailureDoesNotExposeConnectionDetails()
    {
        var result = await OwnerCommandProcess.RunAsync(
            "Host=127.0.0.1;Port=1;Database=mf_unavailable;Username=mf_private;Password=Synthetic-DbSecret!2468;Timeout=1",
            ["owner", "reset-security-stamp", "--email", "owner@example.test"]);

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Output.Contains("Synthetic-", StringComparison.Ordinal),
            "Operational failures must not reveal connection credentials.");
        Assert.False(result.Output.Contains("mf_private", StringComparison.Ordinal),
            "Operational failures must not reveal connection identity.");
        Assert.False(result.Output.Contains("Host=", StringComparison.OrdinalIgnoreCase),
            "Operational failures must not reveal connection details.");
    }
}
