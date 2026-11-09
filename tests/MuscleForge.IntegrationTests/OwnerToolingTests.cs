using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class OwnerToolingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EfDatabaseUpdateWithOwnerCredentialsAppliesSchemaWithoutBootstrappingAnAccount()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        var root = FindRepositoryRoot();
        var startInfo = CreateToolStartInfo(root, database.ConnectionString, "dotnet");
        foreach (var argument in new[]
        {
            "ef", "database", "update",
            "--project", "src/MuscleForge.Infrastructure",
            "--startup-project", "src/MuscleForge.Api",
            "--configuration", "Release", "--no-build"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await RunToolAsync(startInfo);

        Assert.Equal(0, result.ExitCode);
        AssertPrivate(result, database.ConnectionString);
        await using var context = database.CreateContext();
        Assert.Equal(context.Database.GetMigrations(),
            await context.Database.GetAppliedMigrationsAsync(CancellationToken));
        Assert.Empty(await context.Users.ToListAsync(CancellationToken));

        // The migration tool only prepares the schema; normal startup performs bootstrap.
        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString,
            OwnerBootstrapTests.Email, OwnerBootstrapTests.Password);
        Assert.True(api.Ready, "API startup must initialize the owner after explicit migration succeeds.");
        api.AssertPrivate(OwnerBootstrapTests.Password, database.ConnectionString);
        context.ChangeTracker.Clear();
        Assert.Equal(OwnerBootstrapTests.Email, (await context.Users.SingleAsync(CancellationToken)).Email);
    }

    [Fact]
    public async Task OpenApiExportSuppressesAmbientOwnerCredentialsAndLeavesAnEmptyDatabaseUntouched()
    {
        await using var database = await postgres.CreateDatabaseAsync(CancellationToken);
        var root = FindRepositoryRoot();
        var startInfo = CreateToolStartInfo(root, database.ConnectionString, "bash");
        startInfo.ArgumentList.Add(Path.Combine(root, "scripts", "export-openapi.sh"));
        startInfo.ArgumentList.Add("--check");

        var result = await RunToolAsync(startInfo);

        Assert.Equal(0, result.ExitCode);
        AssertPrivate(result, database.ConnectionString);
        Assert.Contains("OpenAPI matches the committed contract", result.Output, StringComparison.Ordinal);
        Assert.Empty(await database.QueryStringsAsync(
            "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public'", CancellationToken));
    }

    private static ProcessStartInfo CreateToolStartInfo(string root, string connectionString, string executable)
    {
        var startInfo = OwnerProcessStartInfo.Create(connectionString, [],
            OwnerBootstrapTests.Email, OwnerBootstrapTests.Password);
        startInfo.FileName = executable;
        startInfo.ArgumentList.Clear();
        startInfo.WorkingDirectory = root;
        return startInfo;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MuscleForge.Backend.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not locate the backend solution for tooling acceptance tests.");
    }

    private static async Task<OwnerCommandResult> RunToolAsync(ProcessStartInfo startInfo)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the tooling acceptance process.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new OwnerCommandResult(process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static void AssertPrivate(OwnerCommandResult result, string connectionString)
    {
        Assert.False(result.Output.Contains(OwnerBootstrapTests.Password, StringComparison.Ordinal),
            "Tooling must not print configured owner credentials.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.False(!string.IsNullOrEmpty(connection.Password)
            && result.Output.Contains(connection.Password, StringComparison.Ordinal),
            "Tooling must not print database credentials.");
        Assert.False(result.Output.Contains("Host=", StringComparison.OrdinalIgnoreCase),
            "Tooling must not print database connection strings.");
    }
}
