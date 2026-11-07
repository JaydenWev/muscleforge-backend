using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MuscleForge.Api.Administration;
using MuscleForge.Infrastructure.Identity;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed partial class AuthenticationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Email = OwnerBootstrapTests.Email;
    private const string Password = OwnerBootstrapTests.Password;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoginMeAndRefreshUseBearerOnlyAndNeverLogCredentialsOrTokens()
    {
        await using var db = await SetupAsync();
        var logs = new CapturedLogs();
        await using var api = CreateApi(db, logs: logs);
        using var client = api.CreateClient();
        using var unauthenticated = await client.GetAsync("/api/v1/auth/me", Ct);
        await Problem(unauthenticated, 401, "authentication_required");
        using var cookieRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        cookieRequest.Headers.Add("Cookie", ".AspNetCore.Identity.Application=synthetic-cookie");
        using var cookie = await client.SendAsync(cookieRequest, Ct);
        await Problem(cookie, 401, "authentication_required");

        var token = await Login(client);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(900, token.ExpiresIn);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(token.RefreshToken));
        using var me = await GetMe(client, token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(Ct));
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = (await users.FindByEmailAsync(Email))!;
            Assert.Equal(owner.Id, body.RootElement.GetProperty("id").GetGuid());
        }
        Assert.Equal(Email, body.RootElement.GetProperty("email").GetString());
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var renewed = (await refresh.Content.ReadFromJsonAsync<Token>(Ct))!;
        using var renewedMe = await GetMe(client, renewed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, renewedMe.StatusCode);
        var text = string.Join('\n', logs.Entries);
        foreach (var secret in new[] { Password, token.AccessToken, token.RefreshToken, renewed.AccessToken, renewed.RefreshToken })
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCredentialsAndTokensUseSafeProblemsAndValidation()
    {
        await using var db = await SetupAsync();
        await using var api = CreateApi(db);
        using var client = api.CreateClient();
        string? loginProblem = null;
        foreach (var email in new[] { Email, "unknown@example.test" })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Synthetic!2468" }, Ct);
            var problem = await Problem(response, 401, "authentication_required");
            Assert.DoesNotContain("Wrong-Synthetic!2468", problem, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(problem);
            var detail = json.RootElement.GetProperty("title").GetString();
            if (loginProblem is null) loginProblem = detail; else Assert.Equal(loginProblem, detail);
        }
        using var malformed = await client.PostAsync("/api/v1/auth/login", new StringContent("{", Encoding.UTF8, "application/json"), Ct);
        await Problem(malformed, 400, "invalid_request");
        using var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" }, Ct);
        var validation = await Problem(invalid, 400, "validation_failed");
        using (var errors = JsonDocument.Parse(validation))
            Assert.True(errors.RootElement.GetProperty("errors").EnumerateObject().Any());
        using var emptyRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "" }, Ct);
        var refreshValidation = await Problem(emptyRefresh, 400, "validation_failed");
        using (var errors = JsonDocument.Parse(refreshValidation))
            Assert.True(errors.RootElement.GetProperty("errors").EnumerateObject().Any());
        using var badRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "synthetic-invalid-refresh" }, Ct);
        var refreshProblem = await Problem(badRefresh, 401, "authentication_required");
        Assert.DoesNotContain("synthetic-invalid-refresh", refreshProblem, StringComparison.Ordinal);
        using var badAccess = await GetMe(client, "synthetic-invalid-access");
        await Problem(badAccess, 401, "authentication_required");
        using var oversized = await client.PostAsync("/api/v1/auth/login", new StringContent(new string('x', 262145), Encoding.UTF8, "application/json"), Ct);
        await Problem(oversized, 413, "request_too_large");
    }

    private async Task<TestDatabase> SetupAsync()
    {
        var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Database"] = db.ConnectionString, ["Owner:Email"] = Email, ["Owner:Password"] = Password }).Build();
            Assert.Equal(OwnerBootstrapResult.Created, await OwnerAccountBootstrapper.EnsureCreatedAsync(config, Ct));
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    private static WebApplicationFactory<Program> CreateApi(TestDatabase db, TimeProvider? clock = null, KeyDirectory? keys = null, CapturedLogs? logs = null) =>
        db.CreateApi().WithWebHostBuilder(builder =>
        {
            if (keys is not null) builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["DataProtection:KeysPath"] = keys.Path, ["DataProtection:CertificatePath"] = keys.CertificatePath, ["DataProtection:CertificatePassword"] = KeyDirectory.Password }));
            if (clock is not null) builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); });
            if (logs is not null) builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static async Task<Token> Login(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Token>(Ct))!;
    }
    private static async Task<HttpResponseMessage> GetMe(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }
    private static async Task<string> Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        return text;
    }
    private sealed record Token(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);
    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
    private sealed class KeyDirectory : IDisposable
    {
        public const string Password = "Synthetic-Certificate!2468";
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mf-auth-" + Guid.NewGuid().ToString("N"));
        public string CertificatePath => System.IO.Path.Combine(Path, "test.pfx");
        public KeyDirectory()
        {
            Directory.CreateDirectory(Path);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MuscleForge integration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            File.WriteAllBytes(CertificatePath, certificate.Export(X509ContentType.Pfx, Password));
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
