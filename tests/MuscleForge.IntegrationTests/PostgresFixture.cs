using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MuscleForge.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    // Match the deployment's pinned PostgreSQL version; Testcontainers chooses a free host port.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.11-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await container.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = $"mf_test_{Guid.NewGuid():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = name,
            Pooling = false
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new TestDatabase(name, connectionString, container.GetConnectionString());
    }
}

public sealed class TestDatabase(string name, string connectionString, string adminConnectionString) : IAsyncDisposable
{
    public string ConnectionString => connectionString;

    public MuscleForgeDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MuscleForgeDbContext>().UseNpgsql(connectionString).Options);

    public WebApplicationFactory<Program> CreateApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Database"] = connectionString }));
        });

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string[]> QueryStringsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }
        return [.. values];
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
