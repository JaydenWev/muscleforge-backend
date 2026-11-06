using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MuscleForge.Api.Contracts;
using MuscleForge.Core.Persistence;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed class ApiTests
{
    [Fact]
    public async Task StatusReportsTheSupportedApiVersion()
    {
        await using var factory = CreateFactory(false);
        using var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<ApiStatusResponse>(
            "/api/v1/status", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("MuscleForge", status!.Service);
        Assert.Equal("v1", status.ApiVersion);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task ReadinessReflectsDatabaseReadiness(bool ready, HttpStatusCode expected)
    {
        await using var factory = CreateFactory(ready);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("Host=", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PublicRegistrationIsUnavailable()
    {
        await using var factory = CreateFactory(true);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "test@example.test" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownProgramRouteUsesTheProblemContractEvenForUnsupportedAccept()
    {
        await using var factory = CreateFactory(true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/missing");
        request.Headers.Accept.ParseAdd("application/xml");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("resource_not_found", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ProgramDoesNotAllowWildcardCors()
    {
        await using var factory = CreateFactory(true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/status");
        request.Headers.Add("Origin", "https://example.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static WebApplicationFactory<Program> CreateFactory(bool ready) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = "Host=localhost;Database=muscleforge;Username=test",
                    ["Owner:Email"] = null,
                    ["Owner:Password"] = null
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseProbe>();
                services.AddSingleton<IDatabaseProbe>(new FakeProbe(ready));
            });
        });

    private sealed class FakeProbe(bool ready) : IDatabaseProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(ready);
    }
}
