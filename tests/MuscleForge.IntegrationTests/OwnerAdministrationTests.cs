using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class OwnerAdministrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Email = "owner@example.test";
    private const string Password = "Synthetic-Owner!2468";
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatePersistsAnIdentityAccountThatCanValidateItsPassword()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email, "--password-stdin"], Password + "\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("created", result.Output, StringComparison.OrdinalIgnoreCase);
        AssertPrivate(result);
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.NotEqual(Guid.Empty, owner.Id);
        Assert.Equal(Email, owner.Email);
        Assert.Equal(Email, owner.UserName);
        Assert.Equal(Email.ToUpperInvariant(), owner.NormalizedEmail);
        Assert.Equal(Email.ToUpperInvariant(), owner.NormalizedUserName);
        Assert.False(string.IsNullOrEmpty(owner.PasswordHash));
        Assert.True(owner.PasswordHash != Password, "Identity must persist a hash, not the password.");
        Assert.False(string.IsNullOrEmpty(owner.SecurityStamp));
        Assert.False(owner.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.True(await CheckPasswordAsync(database, owner, Password));
        Assert.False(await CheckPasswordAsync(database, owner, "Wrong-Synthetic!2468"));
        Assert.Empty(await context.Exercises.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task RepeatedCreationUsesNormalizedEmailAndPreservesTheAccountAndPersonalData()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        Assert.Equal(0, (await CreateOwnerAsync(database)).ExitCode);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        var exercise = new Exercise(before.Id, "Personal squat", "Barbell");
        exercise.Archive(DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        // No password option/input: an existing account must be recognized before prompting.
        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email.ToUpperInvariant()]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("already exists", result.Output, StringComparison.OrdinalIgnoreCase);
        AssertPrivate(result);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        var preservedExercise = await context.Exercises.SingleAsync(CancellationToken);
        Assert.Equal(exercise.Id, preservedExercise.Id);
        Assert.Equal(exercise.Name, preservedExercise.Name);
        Assert.Equal(exercise.ArchivedAt, preservedExercise.ArchivedAt);
    }

    [Fact]
    public async Task ConcurrentCreationReportsOneCreatedAccountAndOneSafeNoOp()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var results = await Task.WhenAll(CreateOwnerAsync(database), CreateOwnerAsync(database));

        Assert.All(results, result =>
        {
            Assert.Equal(0, result.ExitCode);
            AssertPrivate(result);
        });
        Assert.Single(results, result => result.Output.Contains("account created", StringComparison.OrdinalIgnoreCase));
        Assert.Single(results, result => result.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase));
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.True(await CheckPasswordAsync(database, owner, Password));
    }

    [Fact]
    public async Task SecurityStampResetPreservesPasswordAndPersonalData()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        Assert.Equal(0, (await CreateOwnerAsync(database)).ExitCode);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        var exercise = new Exercise(before.Id, "Personal press", "Dumbbells");
        context.Exercises.Add(exercise);
        await context.SaveChangesAsync(CancellationToken);

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", Email.ToUpperInvariant()]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("security stamp reset", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access tokens expire normally", result.Output, StringComparison.OrdinalIgnoreCase);
        AssertPrivate(result);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.False(string.IsNullOrEmpty(after.SecurityStamp));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(exercise.Id, (await context.Exercises.SingleAsync(CancellationToken)).Id);
        Assert.True(await CheckPasswordAsync(database, after, Password));
    }

    [Fact]
    public async Task ResettingAMissingAccountReportsNotFoundWithoutCreatingAnything()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", Email]);

        Assert.Equal(3, result.ExitCode);
        AssertPrivate(result);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Theory]
    [InlineData("Short!24")]
    [InlineData("lowercaseonlypassword")]
    [InlineData("")]
    [InlineData("            ")]
    public async Task InvalidPasswordsAreRejectedWithoutCreatingAnAccountOrEchoingTheInput(string password)
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email, "--password-stdin"], password + "\n");

        Assert.Equal(2, result.ExitCode);
        AssertPrivate(result);
        if (!string.IsNullOrWhiteSpace(password))
        {
            Assert.False(result.Output.Contains(password, StringComparison.Ordinal),
                "Password validation must never echo the rejected password.");
        }
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task OverlongPasswordInputIsBoundedAndRejectedWithoutCreatingAnAccount()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var password = new string('A', 4097) + "!2a";

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email, "--password-stdin"], password + "\n");

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Output.Contains(new string('A', 32), StringComparison.Ordinal),
            "Overlong input must not be echoed, including partial password contents.");
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task PasswordWhitespaceIsPreservedInsteadOfSilentlyTrimmed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var password = "  " + Password + "  ";

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email, "--password-stdin"], password + "\n");

        Assert.Equal(0, result.ExitCode);
        AssertPrivate(result);
        await using var context = database.CreateContext();
        var owner = await context.Users.SingleAsync(CancellationToken);
        Assert.True(await CheckPasswordAsync(database, owner, password));
        Assert.False(await CheckPasswordAsync(database, owner, password.Trim()));
    }

    [Fact]
    public async Task RedirectedInputRequiresTheExplicitPasswordStdinOption()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email], Password + "\n");

        Assert.Equal(2, result.ExitCode);
        AssertPrivate(result);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task InvalidEmailIsRejectedWithoutEchoingCredentialsOrCreatingAnAccount()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", "invalid-email", "--password-stdin"], Password + "\n");

        Assert.Equal(2, result.ExitCode);
        AssertPrivate(result);
        await using var context = database.CreateContext();
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("reset-security-stamp")]
    public async Task AdministrationRefusesAnUnmigratedDatabaseAndDoesNotCreateTheSchema(string command)
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        string[] arguments = command == "create"
            ? ["owner", command, "--email", Email, "--password-stdin"]
            : ["owner", command, "--email", Email];

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString, arguments, Password + "\n");

        Assert.Equal(4, result.ExitCode);
        AssertPrivate(result);
        Assert.Empty(await database.QueryStringsAsync(
            "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public'", CancellationToken));
    }

    [Fact]
    public async Task AdministrationRefusesUnknownMigrationHistoryWithoutMutatingTheAccount()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        Assert.Equal(0, (await CreateOwnerAsync(database)).ExitCode);
        await using var context = database.CreateContext();
        var before = await context.Users.SingleAsync(CancellationToken);
        await database.ExecuteAsync("""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('99991231235959_UnknownSchema', '10.0.14');
            """, CancellationToken);

        var result = await OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "reset-security-stamp", "--email", Email]);

        Assert.Equal(4, result.ExitCode);
        AssertPrivate(result);
        context.ChangeTracker.Clear();
        var after = await context.Users.SingleAsync(CancellationToken);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    private async Task<TestDatabase> CreateMigratedDatabaseAsync()
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

    private static Task<OwnerCommandResult> CreateOwnerAsync(TestDatabase database) =>
        OwnerCommandProcess.RunAsync(database.ConnectionString,
            ["owner", "create", "--email", Email, "--password-stdin"], Password + "\n");

    private static async Task<bool> CheckPasswordAsync(TestDatabase database, ApplicationUser user, string password)
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

    private static void AssertPrivate(OwnerCommandResult result)
    {
        Assert.False(result.Output.Contains(Password, StringComparison.Ordinal), "Passwords must not be printed.");
        Assert.False(result.Output.Contains("Host=", StringComparison.OrdinalIgnoreCase),
            "Connection details must not be printed.");
        Assert.False(result.Output.Contains("Password=", StringComparison.OrdinalIgnoreCase),
            "Connection credentials must not be printed.");
    }
}

public sealed class OwnerAdministrationUsageTests
{
    [Fact]
    public async Task HelpWorksWithoutDatabaseConfigurationAndExitsWithoutStartingAServer()
    {
        var result = await OwnerCommandProcess.RunAsync(null, ["owner", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("reset-security-stamp", result.Output, StringComparison.Ordinal);
        Assert.Contains("--password-stdin", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner", "unknown")]
    [InlineData("owner", "create")]
    [InlineData("owner", "create", "--email")]
    [InlineData("owner", "create", "--email", "owner@example.test", "--email", "another@example.test")]
    [InlineData("owner", "reset-security-stamp", "--email", "owner@example.test", "--password-stdin")]
    [InlineData("owner", "create", "--email", "owner@example.test", "--password", "Synthetic-ArgSecret!2468")]
    [InlineData("owner", "create", "--email", "owner@example.test", "--ConnectionStrings:Database", "Synthetic-ArgSecret!2468")]
    public async Task MalformedArgumentsAreRejectedBeforeConfigurationAndAreNeverEchoed(params string[] arguments)
    {
        var result = await OwnerCommandProcess.RunAsync(null, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Output.Contains("Synthetic-ArgSecret!2468", StringComparison.Ordinal),
            "Unknown argument values must never be echoed.");
    }

    [Fact]
    public async Task ACommandWithoutDatabaseConfigurationReturnsAnOperationalFailure()
    {
        var result = await OwnerCommandProcess.RunAsync(null,
            ["owner", "create", "--email", "owner@example.test", "--password-stdin"], "Synthetic-Owner!2468\n");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Output.Contains("Synthetic-Owner!2468", StringComparison.Ordinal),
            "Configuration failures must not reveal password input.");
    }

    [Fact]
    public async Task DatabaseConnectionFailureDoesNotExposeConnectionDetails()
    {
        var result = await OwnerCommandProcess.RunAsync(
            "Host=127.0.0.1;Port=1;Database=mf_unavailable;Username=mf_private;Password=Synthetic-DbSecret!2468;Timeout=1",
            ["owner", "create", "--email", "owner@example.test", "--password-stdin"], "Synthetic-Owner!2468\n");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Output.Contains("Synthetic-", StringComparison.Ordinal),
            "Operational failures must not reveal passwords or connection credentials.");
        Assert.False(result.Output.Contains("mf_private", StringComparison.Ordinal),
            "Operational failures must not reveal connection identity.");
        Assert.False(result.Output.Contains("Host=", StringComparison.OrdinalIgnoreCase),
            "Operational failures must not reveal connection details.");
    }
}

internal sealed record OwnerCommandResult(int ExitCode, string Output);

internal static class OwnerCommandProcess
{
    public static async Task<OwnerCommandResult> RunAsync(string? connectionString, string[] arguments,
        string? input = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        // Isolate configuration from local developer credentials and environment selection.
        foreach (var key in startInfo.Environment.Keys.Where(key =>
            key.Equals("ConnectionStrings__Database", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ConnectionStrings:Database", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            startInfo.Environment.Remove(key);
        }
        startInfo.Environment["ConnectionStrings__Database"] = connectionString ?? string.Empty;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the owner command.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return new OwnerCommandResult(process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
