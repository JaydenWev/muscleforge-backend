using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
[Trait("Acceptance", "Terminal")]
public sealed class OwnerTerminalAcceptanceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    // A real PTY catches prompt/terminal-mode races that redirected stdin cannot.
    // Windows terminal input requires separate validation; Linux CI runs all cases.
    [Theory(SkipUnless = nameof(IsLinux), Skip = "Linux PTY regression checks; Windows terminal input is not covered.")]
    [InlineData("terminal-create")]
    [InlineData("terminal-mismatch")]
    [InlineData("terminal-cancel")]
    [InlineData("stdin-cancel")]
    public async Task RealTerminalBehavior(string scenario)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await using var database = await postgres.CreateDatabaseAsync(timeout.Token);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(timeout.Token);

        var startInfo = new ProcessStartInfo("python3")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "check-owner-terminal.py"));
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add(scenario);
        startInfo.Environment["ConnectionStrings__Database"] = database.ConnectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start terminal probe.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, output);
            Assert.Contains("Terminal acceptance passed: " + scenario, output);
            var count = await context.Users.CountAsync(timeout.Token);
            Assert.Equal(scenario == "terminal-create" ? 1 : 0, count);
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
