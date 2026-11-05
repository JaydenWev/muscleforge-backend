using System.Diagnostics;
using System.Net;
using System.Text;
using Npgsql;
using Xunit;

namespace MuscleForge.IntegrationTests;

// Real processes exercise environment configuration and initialization before the listener.
// Captured output is inspected without attaching potentially sensitive transcripts to failures.
internal sealed class OwnerApiProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StringBuilder output = new();
    private readonly object outputLock = new();
    private readonly TaskCompletionSource<Uri> listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task[] readers;

    private OwnerApiProcess(ProcessStartInfo startInfo)
    {
        process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start API process.");
        readers = [ReadOutputAsync(process.StandardOutput), ReadOutputAsync(process.StandardError)];
    }

    public bool Ready { get; private set; }
    public Uri? Address { get; private set; }
    public int? ExitCode => process.HasExited ? process.ExitCode : null;
    public string Output
    {
        get
        {
            lock (outputLock)
            {
                return output.ToString();
            }
        }
    }

    public static async Task<OwnerApiProcess> StartAsync(string connectionString, string? email, string? password)
    {
        var startInfo = OwnerProcessStartInfo.Create(connectionString, [], email, password);
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        var api = new OwnerApiProcess(startInfo);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var exited = api.process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(api.listening.Task, exited) == api.listening.Task)
            {
                api.Address = await api.listening.Task;
                using var client = new HttpClient { BaseAddress = api.Address };
                using var response = await client.GetAsync("/api/v1/status", timeout.Token);
                api.Ready = response.StatusCode == HttpStatusCode.OK;
            }
            else
            {
                await exited;
                await Task.WhenAll(api.readers);
            }
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    public static async Task<OwnerApiProcess[]> StartPairAsync(string connectionString,
        string firstEmail, string secondEmail, string password)
    {
        Task<OwnerApiProcess>[] startups =
        [
            StartAsync(connectionString, firstEmail, password),
            StartAsync(connectionString, secondEmail, password)
        ];
        try
        {
            return await Task.WhenAll(startups);
        }
        catch
        {
            // A timed-out peer must not leave the successfully started API running.
            foreach (var startup in startups.Where(startup => startup.IsCompletedSuccessfully))
            {
                var api = await startup;
                await api.DisposeAsync();
            }
            throw;
        }
    }

    public void AssertPrivate(string? password, string connectionString)
    {
        var captured = Output;
        if (!string.IsNullOrWhiteSpace(password))
        {
            Assert.False(captured.Contains(password, StringComparison.Ordinal), "Owner credentials must not be printed.");
        }
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(connection.Password))
        {
            Assert.False(captured.Contains(connection.Password, StringComparison.Ordinal),
                "Database credentials must not be printed.");
        }
        Assert.False(captured.Contains("Host=", StringComparison.OrdinalIgnoreCase),
            "Database connection strings must not be printed.");
        Assert.False(captured.Contains("Npgsql", StringComparison.Ordinal), "Database inner exceptions must not be printed.");
        Assert.False(captured.Contains("DbUpdateException", StringComparison.Ordinal),
            "Persistence inner exceptions must not be printed.");
    }

    private async Task ReadOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (outputLock)
            {
                output.AppendLine(line);
            }
            const string marker = "Now listening on: ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var address))
            {
                listening.TrySetResult(address);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
        await Task.WhenAll(readers);
        process.Dispose();
    }
}

internal static class OwnerProcessStartInfo
{
    public static ProcessStartInfo Create(string? connectionString, string[] arguments,
        string? ownerEmail = null, string? ownerPassword = null)
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
        foreach (var key in startInfo.Environment.Keys.Where(key =>
            key.Equals("ConnectionStrings__Database", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ConnectionStrings:Database", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Owner__", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Owner:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            startInfo.Environment.Remove(key);
        }
        startInfo.Environment["ConnectionStrings__Database"] = connectionString ?? string.Empty;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        if (ownerEmail is not null)
        {
            startInfo.Environment["Owner__Email"] = ownerEmail;
        }
        if (ownerPassword is not null)
        {
            startInfo.Environment["Owner__Password"] = ownerPassword;
        }
        return startInfo;
    }
}

internal sealed record OwnerCommandResult(int ExitCode, string Output);

internal static class OwnerCommandProcess
{
    public static async Task<OwnerCommandResult> RunAsync(string? connectionString, string[] arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = Process.Start(OwnerProcessStartInfo.Create(connectionString, arguments))
            ?? throw new InvalidOperationException("Could not start the owner command.");
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
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
