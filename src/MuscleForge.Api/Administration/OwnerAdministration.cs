using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;
using Npgsql;

namespace MuscleForge.Api.Administration;

internal static class OwnerAdministration
{
    private const int MaximumPasswordLength = 4096;
    private const string Usage = """
        Usage:
          owner create --email <email> [--password-stdin]
          owner reset-security-stamp --email <email>
          owner --help

        Create reads a hidden password and confirmation from the terminal.
        --password-stdin reads one password line from redirected standard input.
        Passwords require 12-4096 characters, uppercase, lowercase, a digit and a non-alphanumeric character.
        Configure ConnectionStrings:Database and apply migrations before running these commands.
        Exit codes: 0 success/existing account/help; 2 invalid input; 3 account not found;
                    4 configuration/database/operation failure; 130 cancelled.
        """;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is ["--help"])
        {
            Console.Out.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var command))
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
            // Do not feed administrative arguments into configuration or start a web host.
            // Match WebApplication's environment precedence while keeping API user secrets.
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
            var existing = await users.FindByEmailAsync(command.Email);
            if (command.ResetSecurityStamp)
            {
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

            if (existing is not null)
            {
                WriteAlreadyExists();
                return 0;
            }

            var password = await ReadPasswordAsync(command.PasswordStdin, cancellation.Token);
            if (password is null)
            {
                Console.Error.WriteLine("Password input is missing, too long, or does not match confirmation. Use a terminal or explicit --password-stdin.");
                return 2;
            }

            cancellation.Token.ThrowIfCancellationRequested();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = command.Email,
                Email = command.Email
            };
            IdentityResult created;
            try
            {
                created = await users.CreateAsync(user, password);
            }
            catch (DbUpdateException exception) when
                (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The normalized username index arbitrates concurrent creation of this email.
                database.ChangeTracker.Clear();
                if (await users.FindByEmailAsync(command.Email) is not null)
                {
                    WriteAlreadyExists();
                    return 0;
                }

                throw;
            }

            if (!created.Succeeded)
            {
                // Another process may have completed between our first lookup and Identity validation.
                if (await users.FindByEmailAsync(command.Email) is not null)
                {
                    WriteAlreadyExists();
                    return 0;
                }

                // Identity descriptions can contain supplied values. Keep administrative output fixed.
                Console.Error.WriteLine("Account validation failed. Use a valid email and a password of 12-4096 characters containing uppercase, lowercase, digit and non-alphanumeric characters.");
                return 2;
            }

            Console.Out.WriteLine("Owner account created.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Owner administration cancelled.");
            return 130;
        }
        catch (Exception)
        {
            // Connection and Identity exceptions may contain credentials or supplied input.
            Console.Error.WriteLine("Owner administration failed. Check database connectivity and configuration, then retry.");
            return 4;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static bool TryParse(string[] args, out Command command)
    {
        command = default;
        if (args.Length == 0 || args[0] is not ("create" or "reset-security-stamp"))
        {
            return false;
        }

        string? email = null;
        var passwordStdin = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--email" && email is null && index + 1 < args.Length)
            {
                email = args[++index];
            }
            else if (args[index] == "--password-stdin" && !passwordStdin && args[0] == "create")
            {
                passwordStdin = true;
            }
            else
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || email != email.Trim()
            || email.Any(char.IsControl) || !new EmailAddressAttribute().IsValid(email))
        {
            return false;
        }

        command = new Command(email, args[0] == "reset-security-stamp", passwordStdin);
        return true;
    }

    private static async Task<string?> ReadPasswordAsync(bool fromStdin, CancellationToken cancellationToken)
    {
        if (fromStdin)
        {
            if (!Console.IsInputRedirected)
            {
                return null;
            }

            // Console.In's synchronized reader may block even through ReadAsync.
            // Keep that read on a background worker so cancellation can exit the command.
            return await Task.Run(() => ReadStdinPassword(cancellationToken), CancellationToken.None)
                .WaitAsync(cancellationToken);
        }

        if (Console.IsInputRedirected)
        {
            return null;
        }

        // Initialize terminal input before publishing the prompt. An immediate paste
        // must not reach the terminal while its default echo mode is still enabled.
        _ = Console.KeyAvailable;
        Console.Error.Write("Password: ");
        var password = await ReadHiddenLineAsync(cancellationToken);
        Console.Error.WriteLine();
        if (password is null)
        {
            return null;
        }

        _ = Console.KeyAvailable;
        Console.Error.Write("Confirm password: ");
        var confirmation = await ReadHiddenLineAsync(cancellationToken);
        Console.Error.WriteLine();
        return password == confirmation ? password : null;
    }

    private static string? ReadStdinPassword(CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = Console.In.Read();
            if (character is -1 or '\r' or '\n')
            {
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (buffer.Length == MaximumPasswordLength)
            {
                return null;
            }

            buffer.Append((char)character);
        }
    }

    private static async Task<string?> ReadHiddenLineAsync(CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Console.KeyAvailable)
            {
                await Task.Delay(25, cancellationToken);
                continue;
            }

            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                if (buffer.Length == MaximumPasswordLength)
                {
                    return null;
                }

                buffer.Append(key.KeyChar);
            }
        }
    }

    private static void WriteAlreadyExists() =>
        Console.Out.WriteLine("Owner account already exists; no changes made.");

    private readonly record struct Command(string Email, bool ResetSecurityStamp, bool PasswordStdin);
}
