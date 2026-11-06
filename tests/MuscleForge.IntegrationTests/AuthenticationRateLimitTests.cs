using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed partial class AuthenticationTests
{
    [Fact]
    public async Task RateLimitPartitionsByRemoteIp()
    {
        await using var db = await SetupAsync();
        await using var api = CreateApi(db);
        _ = api.CreateClient();
        var server = api.Server;
        async Task<Microsoft.AspNetCore.Http.HttpContext> Send(string ip) => await server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyFeature());
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/auth/login";
            context.Request.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes("{\"email\":\"unknown@example.test\",\"password\":\"Wrong-Synthetic!2468\"}");
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }, Ct);
        for (var i = 0; i < 10; i++) Assert.Equal(401, (await Send("192.0.2.1")).Response.StatusCode);
        Assert.Equal(429, (await Send("192.0.2.1")).Response.StatusCode);
        Assert.Equal(401, (await Send("192.0.2.2")).Response.StatusCode);
    }

    [Theory]
    [InlineData("login", 10)]
    [InlineData("refresh", 30)]
    public async Task AuthEndpointsRateLimitWithoutQueuing(string endpoint, int limit)
    {
        await using var db = await SetupAsync();
        await using var api = CreateApi(db);
        using var client = api.CreateClient();
        for (var i = 0; i < limit; i++)
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/auth/{endpoint}", endpoint == "login"
                ? (object)new { email = "unknown@example.test", password = "Wrong-Synthetic!2468" }
                : new { refreshToken = "synthetic-invalid-refresh" }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync($"/api/v1/auth/{endpoint}", endpoint == "login"
            ? (object)new { email = "unknown@example.test", password = "Wrong-Synthetic!2468" }
            : new { refreshToken = "synthetic-invalid-refresh" }, Ct);
        await Problem(limited, 429, "rate_limited");
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    private sealed class RequestBodyFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
