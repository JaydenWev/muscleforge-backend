using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Api.Administration;

internal static class OwnerAdministration
{
    private const string Usage = """
        Usage:
          owner reset-security-stamp --email <email>
          owner --help

        Configure ConnectionStrings:Database and apply migrations before resetting a stamp.
        Account creation uses Owner:Email and Owner:Password during API startup.
        Existing access tokens expire normally after a security-stamp reset.
        Exit codes: 0 success/help; 2 invalid input; 3 account not found;
                    4 configuration/database/operation failure; 130 cancelled.
        """;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is ["--help"])
        {
            Console.Out.WriteLine(Usage);
            return 0;
        }

        if (args is not ["reset-security-stamp", "--email", var email]
            || !OwnerAccountBootstrapper.IsValidEmail(email))
        {
            Console.Error.WriteLine("Invalid owner command. Run 'owner --help' for usage.");
            return 2;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            // Administrative arguments never enter configuration and this host never starts.
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [],
                ApplicationName = typeof(OwnerAdministration).Assembly.GetName().Name,
                EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            });
            builder.Logging.ClearProviders();
            var connectionString = builder.Configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine("Configure ConnectionStrings:Database before running owner administration.");
                return 4;
            }

            builder.Services.AddDbContext<MuscleForgeDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddMuscleForgeIdentity();
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MuscleForgeDbContext>();
            if (!await new PostgresDatabaseProbe(database).IsReadyAsync(cancellation.Token))
            {
                Console.Error.WriteLine("Database is unavailable or its schema is not ready. Apply migrations explicitly before retrying.");
                return 4;
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            cancellation.Token.ThrowIfCancellationRequested();
            var existing = await users.FindByEmailAsync(email);
            if (existing is null)
            {
                Console.Error.WriteLine("Owner account was not found; no changes made.");
                return 3;
            }

            cancellation.Token.ThrowIfCancellationRequested();
            var result = await users.UpdateSecurityStampAsync(existing);
            if (!result.Succeeded)
            {
                Console.Error.WriteLine("Account security stamp could not be updated; retry after checking account configuration.");
                return 4;
            }

            Console.Out.WriteLine("Account security stamp reset. Existing access tokens expire normally.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Owner administration cancelled.");
            return 130;
        }
        catch (Exception)
        {
            // Exceptions may contain connection details or supplied values.
            Console.Error.WriteLine("Owner administration failed. Check database connectivity and configuration, then retry.");
            return 4;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
