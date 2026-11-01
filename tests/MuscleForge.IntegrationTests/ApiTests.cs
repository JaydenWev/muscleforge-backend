using System.Net;
using System.Net.Http.Json;
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
    public async Task ReadinessReflectsDatabaseAvailability(bool connected, HttpStatusCode expected)
    {
        await using var factory = CreateFactory(connected);
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

    private static WebApplicationFactory<Program> CreateFactory(bool connected) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = "Host=localhost;Database=muscleforge;Username=test"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseProbe>();
                services.AddSingleton<IDatabaseProbe>(new FakeProbe(connected));
            });
        });

    private sealed class FakeProbe(bool connected) : IDatabaseProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(connected);
    }
}
