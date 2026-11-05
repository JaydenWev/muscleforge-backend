using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class OwnerBootstrapTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    internal const string Email = "owner@example.test";
    internal const string Password = "Synthetic-Owner!2468";
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartupCreatesTheConfiguredIdentityOwnerBeforeServingRequests()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, Password);

        Assert.True(api.Ready, "Configured startup must serve requests after owner initialization.");
        api.AssertPrivate(Password, database.ConnectionString);
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.NotEqual(Guid.Empty, owner.Id);
        Assert.Equal(Email, owner.Email);
        Assert.Equal(Email, owner.UserName);
        Assert.Equal(Email.ToUpperInvariant(), owner.NormalizedEmail);
        Assert.Equal(Email.ToUpperInvariant(), owner.NormalizedUserName);
        Assert.False(string.IsNullOrEmpty(owner.PasswordHash));
        Assert.True(owner.PasswordHash != Password, "Identity must persist a hash, not the configured password.");
        Assert.False(string.IsNullOrEmpty(owner.SecurityStamp));
        Assert.True(await CheckPasswordAsync(database, owner, Password));
        Assert.False(await CheckPasswordAsync(database, owner, "Wrong-Synthetic!2468"));
        Assert.Empty(await context.Exercises.ToListAsync(CancellationToken));

        using var client = new HttpClient { BaseAddress = api.Address };
        foreach (var route in new[] { "/register", "/api/v1/auth/register" })
        {
            using var response = await client.PostAsJsonAsync(route,
                new { email = "unwanted@example.test" }, CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("Changed-Synthetic!2468")]
    public async Task RestartUsesNormalizedEmailAndPreservesExistingAccountAndPersonalData(string changedPassword)
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        await BootstrapOwnerAsync(database);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        var exercise = new Exercise(before.Id, "Personal squat", "Barbell");
        exercise.Archive(DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString,
            Email.ToUpperInvariant(), changedPassword);

        Assert.True(api.Ready, "An existing matching account must be accepted before password validation.");
        api.AssertPrivate(changedPassword, database.ConnectionString);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.True(await CheckPasswordAsync(database, after, Password));
        Assert.False(await CheckPasswordAsync(database, after, changedPassword));
        var preservedExercise = await context.Exercises.SingleAsync(CancellationToken);
        Assert.Equal(exercise.Id, preservedExercise.Id);
        Assert.Equal(exercise.Name, preservedExercise.Name);
        Assert.Equal(exercise.ArchivedAt, preservedExercise.ArchivedAt);
    }

    [Fact]
    public async Task ConcurrentStartupsForTheSameOwnerBothSucceedAndCreateOneAccount()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        var startups = await OwnerApiProcess.StartPairAsync(database.ConnectionString,
            Email, Email.ToUpperInvariant(), Password);
        await using var first = startups[0];
        await using var second = startups[1];

        Assert.True(first.Ready && second.Ready, "Concurrent identical owner provisioning must be repeatable.");
        first.AssertPrivate(Password, database.ConnectionString);
        second.AssertPrivate(Password, database.ConnectionString);
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.True(await CheckPasswordAsync(database, owner, Password));
    }

    [Fact]
    public async Task ConcurrentStartupsForDifferentOwnersCreateOnlyOneAccount()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        var startups = await OwnerApiProcess.StartPairAsync(database.ConnectionString,
            Email, "different@example.test", Password);
        await using var first = startups[0];
        await using var second = startups[1];

        Assert.Single(startups, api => api.Ready);
        var refused = Assert.Single(startups, api => !api.Ready);
        Assert.True(refused.ExitCode is not null and not 0, "Conflicting owner initialization must fail before listening.");
        first.AssertPrivate(Password, database.ConnectionString);
        second.AssertPrivate(Password, database.ConnectionString);
        await using var context = database.CreateContext();
        Assert.Single(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task ConfiguringADifferentEmailDoesNotCreateASecondOwnerOrReplacePersonalData()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        await BootstrapOwnerAsync(database);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        var exercise = new Exercise(before.Id, "Personal press", "Dumbbells");
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString,
            "different@example.test", Password);

        AssertFailedSafely(api, Password, database.ConnectionString);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(exercise.Id, (await context.Exercises.SingleAsync(CancellationToken)).Id);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("complexity")]
    [InlineData("whitespace")]
    [InlineData("overlong")]
    public async Task InvalidFreshOwnerPasswordsFailBeforeListeningWithoutCreatingAnAccount(string example)
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        var password = example switch
        {
            "short" => "Short!24",
            "complexity" => "lowercaseonlypassword",
            "whitespace" => new string(' ', 12),
            _ => new string('A', 4097) + "!2a"
        };

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, password);

        AssertFailedSafely(api, password, database.ConnectionString);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData(" owner@example.test")]
    [InlineData(" ")]
    public async Task InvalidOwnerEmailsFailWithoutCreatingAnAccount(string email)
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, email, Password);

        AssertFailedSafely(api, Password, database.ConnectionString);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task ConfiguredPasswordWhitespaceIsPreservedInsteadOfSilentlyTrimmed()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        var password = "  " + Password + "  ";

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, password);

        Assert.True(api.Ready);
        api.AssertPrivate(password, database.ConnectionString);
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.True(await CheckPasswordAsync(database, owner, password));
        Assert.False(await CheckPasswordAsync(database, owner, password.Trim()));
    }

    [Fact]
    public async Task ConfiguredStartupRefusesAnUnmigratedDatabaseWithoutCreatingTheSchema()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, Password);

        AssertFailedSafely(api, Password, database.ConnectionString);
        Assert.Empty(await database.QueryStringsAsync(
            "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public'", CancellationToken));
    }

    [Fact]
    public async Task ConfiguredStartupRefusesUnknownMigrationHistoryBeforeChangingTheOwner()
    {
        await using var database = await CreateMigratedDatabaseAsync(postgres);
        await BootstrapOwnerAsync(database);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        await database.ExecuteAsync("""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('99991231235959_UnknownSchema', '10.0.14');
            """, CancellationToken);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, "weak");

        AssertFailedSafely(api, "weak", database.ConnectionString);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
    }

    internal static async Task<TestDatabase> CreateMigratedDatabaseAsync(PostgresFixture postgres)
    {
        var database = await postgres.CreateDatabaseAsync(CancellationToken);
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync(CancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    internal static async Task BootstrapOwnerAsync(TestDatabase database)
    {
        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString, Email, Password);
        Assert.True(api.Ready, "Initial owner setup must succeed.");
    }

    internal static async Task<bool> CheckPasswordAsync(TestDatabase database, ApplicationUser user, string password)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MuscleForgeDbContext>(options => options.UseNpgsql(database.ConnectionString));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<MuscleForgeDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .CheckPasswordAsync(user, password);
    }

    internal static void AssertFailedSafely(OwnerApiProcess api, string password, string connectionString)
    {
        Assert.False(api.Ready, "Invalid owner initialization must fail before the API listens.");
        Assert.True(api.ExitCode is not null and not 0, "Invalid owner initialization must have a nonzero exit code.");
        api.AssertPrivate(password, connectionString);
        Assert.Contains("Owner account initialization failed", api.Output, StringComparison.Ordinal);
    }
}

public sealed class OwnerBootstrapConfigurationTests
{
    private const string UnavailableDatabase =
        "Host=127.0.0.1;Port=1;Database=mf_unavailable;Username=mf_private;Password=Synthetic-DbSecret!2468;Timeout=1";

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public async Task AbsentOwnerConfigurationAllowsStatusWithoutInitializingTheDatabase(string? email, string? password)
    {
        await using var api = await OwnerApiProcess.StartAsync(UnavailableDatabase, email, password);

        Assert.True(api.Ready, "Optional owner initialization must not require a database connection when absent.");
        api.AssertPrivate(password, UnavailableDatabase);
    }

    [Theory]
    [InlineData("owner@example.test", null)]
    [InlineData(null, "Synthetic-Owner!2468")]
    [InlineData("owner@example.test", "")]
    [InlineData("", "Synthetic-Owner!2468")]
    [InlineData(" ", " ")]
    public async Task PartialOrWhitespaceCredentialsFailBeforeListeningWithoutExposingSecrets(string? email, string? password)
    {
        await using var api = await OwnerApiProcess.StartAsync(UnavailableDatabase, email, password);

        OwnerBootstrapTests.AssertFailedSafely(api, password ?? string.Empty, UnavailableDatabase);
    }

    [Fact]
    public async Task OwnerInitializationDatabaseFailureDoesNotExposeConnectionCredentialsOrInnerExceptions()
    {
        await using var api = await OwnerApiProcess.StartAsync(UnavailableDatabase,
            OwnerBootstrapTests.Email, OwnerBootstrapTests.Password);

        OwnerBootstrapTests.AssertFailedSafely(api, OwnerBootstrapTests.Password, UnavailableDatabase);
    }
}
