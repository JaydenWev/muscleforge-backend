using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Api.Administration;

public enum OwnerBootstrapResult
{
    NotConfigured,
    Created,
    AlreadyExists
}

public static class OwnerAccountBootstrapper
{
    public const string FailureMessage =
        "Owner account initialization failed. Check owner credentials, database connectivity and applied migrations.";

    public static async Task<OwnerBootstrapResult> EnsureCreatedAsync(
        IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var email = configuration["Owner:Email"];
            var password = configuration["Owner:Password"];
            if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(password))
            {
                return OwnerBootstrapResult.NotConfigured;
            }

            if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException();
            }

            var connectionString = configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException();
            }

            // Isolate provisioning from the API's logging providers. Framework diagnostics
            // and exception details must not expose configured credentials during startup.
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.ClearProviders());
            services.AddDbContext<MuscleForgeDbContext>(options => options.UseNpgsql(connectionString)
                .EnableSensitiveDataLogging(false).EnableDetailedErrors(false));
            services.AddMuscleForgeIdentity();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MuscleForgeDbContext>();
            if (!await new PostgresDatabaseProbe(database).IsReadyAsync(cancellationToken))
            {
                throw new InvalidOperationException();
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", cancellationToken);
            // One bootstrap critical section across API instances, including differently
            // configured emails. PostgreSQL releases this fixed resource lock with the transaction.
            await database.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(5069152547284382290)", cancellationToken);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            cancellationToken.ThrowIfCancellationRequested();
            if (await users.FindByEmailAsync(email!) is not null)
            {
                // Config changes must never replace the existing account's password or data.
                await transaction.CommitAsync(cancellationToken);
                return OwnerBootstrapResult.AlreadyExists;
            }

            if (await database.Users.AnyAsync(cancellationToken))
            {
                // A different configured email is not permission to create a second owner.
                throw new InvalidOperationException();
            }

            if (password.Length > 4096)
            {
                throw new InvalidOperationException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email
            };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException();
            }

            await transaction.CommitAsync(cancellationToken);
            return OwnerBootstrapResult.Created;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not retain the original exception: hosting logs inner exceptions on failure.
            throw new InvalidOperationException(FailureMessage);
        }
    }

    internal static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 256 && email == email.Trim()
        && !email.Any(char.IsControl) && new EmailAddressAttribute().IsValid(email);
}
