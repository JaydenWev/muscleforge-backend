using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuscleForge.Api.Standards;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed class ApiStandardsTests
{
    [Theory]
    [InlineData("/validation", 400, "validation_failed")]
    [InlineData("/status/401", 401, "authentication_required")]
    [InlineData("/status/403", 403, "access_denied")]
    [InlineData("/status/404", 404, "resource_not_found")]
    [InlineData("/status/405", 405, "method_not_allowed")]
    [InlineData("/status/409", 409, "conflict")]
    [InlineData("/status/415", 415, "unsupported_media_type")]
    [InlineData("/status/429", 429, "rate_limited")]
    [InlineData("/status/503", 503, "service_unavailable")]
    [InlineData("/version", 409, "version_conflict")]
    [InlineData("/state", 409, "state_conflict")]
    [InlineData("/throw", 500, "internal_error")]
    public async Task SharedPipelineReturnsStableProblemDetails(string path, int status, string code)
    {
        await using var host = await StandardsHost.StartAsync();
        using var response = await host.Client.GetAsync(path, TestContext.Current.CancellationToken);
        using var problem = await AssertProblem(response, status, code);
        Assert.Null(response.Headers.Location);
        if (code == "validation_failed")
            Assert.NotEmpty(problem.RootElement.GetProperty("errors").GetProperty("name").EnumerateArray());
        Assert.DoesNotContain("private-exception-message", problem.RootElement.GetRawText());
    }

    [Fact]
    public async Task MalformedJsonUsesInvalidRequestProblem()
    {
        await using var host = await StandardsHost.StartAsync();
        using var response = await host.Client.PostAsync("/json", new StringContent("{", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        using var problem = await AssertProblem(response, 400, "invalid_request");
    }

    [Fact]
    public async Task RoutingAndBindingFailuresUseTheSharedContract()
    {
        await using var host = await StandardsHost.StartAsync();
        using var wrongMethod = await host.Client.PostAsync("/version", null, TestContext.Current.CancellationToken);
        using var methodProblem = await AssertProblem(wrongMethod, 405, "method_not_allowed");
        using var wrongMedia = await host.Client.PostAsync("/json", new StringContent("{}", Encoding.UTF8, "text/plain"), TestContext.Current.CancellationToken);
        using var mediaProblem = await AssertProblem(wrongMedia, 415, "unsupported_media_type");
    }

    [Theory]
    [InlineData(false, 262144, 200)]
    [InlineData(false, 262145, 413)]
    [InlineData(true, 262144, 200)]
    [InlineData(true, 262145, 413)]
    public async Task BodyLimitAppliesAtBoundaryToKnownAndChunkedLengths(bool chunked, int size, int expected)
    {
        await using var host = await StandardsHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/body") { Content = new SizedContent(size, chunked) };
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 413)
        {
            using var problem = await AssertProblem(response, 413, "request_too_large");
        }
    }

    [Theory]
    [InlineData(262144, 200)]
    [InlineData(262145, 413)]
    public async Task StreamingLimitAlsoAppliesWithoutKestrelLimit(int size, int expected)
    {
        await using var host = await StandardsHost.StartAsync(testServer: true);
        using var response = await host.Client.PostAsync("/body", new SizedContent(size, true), TestContext.Current.CancellationToken);
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 413) using (await AssertProblem(response, 413, "request_too_large")) { }
    }

    [Fact]
    public async Task OversizedJsonBindingAndBodyReaderAreLimited()
    {
        await using var host = await StandardsHost.StartAsync(testServer: true);
        using var json = new UnknownLengthJsonContent("{\"name\":\"" + new string('a', 262144) + "\"}");
        using var jsonResponse = await host.Client.PostAsync("/json", json, TestContext.Current.CancellationToken);
        using var jsonProblem = await AssertProblem(jsonResponse, 413, "request_too_large");
        using var readerResponse = await host.Client.PostAsync("/reader", new SizedContent(262145, true), TestContext.Current.CancellationToken);
        using var readerProblem = await AssertProblem(readerResponse, 413, "request_too_large");
    }

    [Fact]
    public async Task AuthenticationChallengeAndForbidUseTheCommonContract()
    {
        await using var host = await StandardsHost.StartAsync();
        using var challenge = await host.Client.GetAsync("/protected", TestContext.Current.CancellationToken);
        using var challengeProblem = await AssertProblem(challenge, 401, "authentication_required");
        Assert.Null(challenge.Headers.Location);
        Assert.Contains(challenge.Headers.WwwAuthenticate, value => value.Scheme == "Test");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Add("X-Test-Authenticated", "yes");
        using var forbid = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        using var forbidProblem = await AssertProblem(forbid, 403, "access_denied");
        Assert.Null(forbid.Headers.Location);
    }

    [Theory]
    [InlineData(401, "authentication_required", "WWW-Authenticate")]
    [InlineData(409, "conflict", "Allow")]
    public async Task ExplicitEmptyResponsesAreStandardizedAndPreserveHeaders(int status, string code, string header)
    {
        await using var host = await StandardsHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/empty/{status}");
        request.Headers.Accept.ParseAdd("application/xml");
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        using var problem = await AssertProblem(response, status, code);
        Assert.True(header == "Allow" ? response.Content.Headers.Contains(header) : response.Headers.Contains(header));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ValidationFilterEnforcesDtoBoundsAndReturnsJsonPropertyNames()
    {
        await using var host = await StandardsHost.StartAsync();
        Assert.Equal(100, ApiStandards.MaxNameLength);
        Assert.Equal(100, ApiStandards.MaxEquipmentLength);
        Assert.Equal(2000, ApiStandards.MaxNotesLength);
        Assert.Equal(50, ApiStandards.MaxExercisesPerSession);
        Assert.Equal(50, ApiStandards.MaxSetsPerExercise);
        var valid = new BoundedRequest(new string('n', 100), new string('e', 100), new string('x', 2000), new int[50], new int[50]);
        using var validResponse = await host.Client.PostAsJsonAsync("/validate", valid, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        var invalid = new BoundedRequest(new string('n', 101), new string('e', 101), new string('x', 2001), new int[51], new int[51]);
        using var response = await host.Client.PostAsJsonAsync("/validate", invalid, TestContext.Current.CancellationToken);
        using var problem = await AssertProblem(response, 400, "validation_failed");
        var errors = problem.RootElement.GetProperty("errors");
        foreach (var key in new[] { "name", "equipment", "notes", "exercises", "sets" })
            Assert.NotEmpty(errors.GetProperty(key).EnumerateArray());
    }

    [Fact]
    public async Task CompletionLogContainsSafeStructuredFieldsAndNoRequestSecrets()
    {
        await using var host = await StandardsHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/log/private-path-secret?token=private-query-secret")
        {
            Content = new StringContent("private-body-note-secret")
        };
        request.Headers.Add("Authorization", "Bearer private-header-secret");
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        using var problem = await AssertProblem(response, 500, "internal_error");
        var trace = problem.RootElement.GetProperty("traceId").GetString();
        await host.LogCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var completion = Assert.Single(host.Logs.Entries, e => e.Properties.ContainsKey("DurationMs") && Equals(e.Properties.GetValueOrDefault("TraceId"), trace));
        Assert.Equal("/log/{value}", completion.Properties["Route"]);
        Assert.Equal(500, completion.Properties["StatusCode"]);
        Assert.Equal("internal_error", completion.Properties["ErrorCode"]);
        Assert.True(Convert.ToDouble(completion.Properties["DurationMs"]) >= 0);
        var allLogs = string.Join("\n", host.Logs.Entries.Select(e => e.Text));
        foreach (var secret in new[] { "private-path-secret", "private-query-secret", "private-header-secret", "private-body-note-secret", "private-exception-message" })
            Assert.DoesNotContain(secret, allLogs);
    }

    [Fact]
    public async Task FailureAfterResponseStartsAbortsWithoutWritingAProblemBody()
    {
        await using var host = await StandardsHost.StartAsync();
        using var response = await host.Client.GetAsync("/partial", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        host.PartialRelease.TrySetResult();
        await host.PartialCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var completion = Assert.Single(host.Logs.Entries, e => Equals(e.Properties.GetValueOrDefault("Route"), "/partial"));
        Assert.Equal("response_aborted", completion.Properties["ErrorCode"]);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private-exception-message", string.Join("\n", host.Logs.Entries.Select(e => e.Text)));
    }

    [Fact]
    public async Task AbortedRequestIsNotLoggedAsAnInternalError()
    {
        await using var host = await StandardsHost.StartAsync();
        using var cancellation = new CancellationTokenSource();
        var request = host.Client.GetAsync("/cancel", cancellation.Token);
        await host.CancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await host.CancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        // Wait for the completion middleware, rather than racing its finally block.
        await host.CancelCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var completion = Assert.Single(host.Logs.Entries, e => Equals(e.Properties.GetValueOrDefault("Route"), "/cancel"));
        Assert.Equal(499, completion.Properties["StatusCode"]);
        Assert.Equal("request_cancelled", completion.Properties["ErrorCode"]);
        Assert.DoesNotContain("private-cancel-message", string.Join("\n", host.Logs.Entries.Select(e => e.Text)));
    }

    private static async Task<JsonDocument> AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        return body;
    }

    private sealed class StandardsHost(WebApplication app, HttpClient client, CapturingLoggerProvider logs) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public CapturingLoggerProvider Logs { get; } = logs;
        public TaskCompletionSource PartialRelease { get; private init; } = null!;
        public TaskCompletionSource PartialCompleted { get; private init; } = null!;
        public TaskCompletionSource LogCompleted { get; private init; } = null!;
        public TaskCompletionSource CancelStarted { get; private init; } = null!;
        public TaskCompletionSource CancelObserved { get; private init; } = null!;
        public TaskCompletionSource CancelCompleted { get; private init; } = null!;

        public static async Task<StandardsHost> StartAsync(bool testServer = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            var logs = new CapturingLoggerProvider();
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            builder.Logging.AddProvider(logs);
            if (testServer) builder.WebHost.UseTestServer();
            else builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddApiStandards();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            var partialRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var partialCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var logCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            logs.OnEntry = entry => { if (Equals(entry.Properties.GetValueOrDefault("Route"), "/partial")) partialCompleted.TrySetResult(); if (Equals(entry.Properties.GetValueOrDefault("Route"), "/cancel")) cancelCompleted.TrySetResult(); if (Equals(entry.Properties.GetValueOrDefault("Route"), "/log/{value}")) logCompleted.TrySetResult(); };
            app.UseApiStandards();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/protected", () => Results.Ok()).RequireAuthorization(policy => policy.RequireRole("owner"));
            app.MapGet("/validation", () => ApiErrors.Validation(new Dictionary<string, string[]> { ["name"] = ["Name is required."] }));
            app.MapGet("/status/{status:int}", (int status) => Results.StatusCode(status));
            app.MapGet("/version", () => ApiErrors.VersionConflict());
            app.MapGet("/state", () => ApiErrors.StateConflict());
            app.MapGet("/throw", () => Throw());
            app.MapGet("/partial", async (HttpContext context) =>
            {
                await context.Response.WriteAsync("partial");
                await context.Response.Body.FlushAsync();
                await partialRelease.Task;
                throw new InvalidOperationException("private-exception-message");
            });
            app.MapGet("/cancel", async (HttpContext context) =>
            {
                cancelStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) { cancelObserved.TrySetResult(); throw new OperationCanceledException("private-cancel-message", context.RequestAborted); }
            });
            app.MapPost("/log/{value}", async (HttpContext context) => { await context.Request.Body.CopyToAsync(Stream.Null); return Throw(); });
            app.MapPost("/json", (Payload payload) => Results.Ok(payload));
            app.MapPost("/validate", (BoundedRequest payload) => Results.Ok()).WithApiValidation<BoundedRequest>();
            app.MapGet("/empty/{status:int}", (HttpContext context, int status) =>
            {
                context.Response.StatusCode = status;
                context.Response.ContentLength = 0;
                context.Response.Headers[status == 401 ? "WWW-Authenticate" : "Allow"] = status == 401 ? "Bearer" : "GET";
                return Task.CompletedTask;
            });
            app.MapPost("/reader", async (HttpContext context) =>
            {
                while (true)
                {
                    var read = await context.Request.BodyReader.ReadAsync();
                    context.Request.BodyReader.AdvanceTo(read.Buffer.End);
                    if (read.IsCompleted) break;
                }
                return Results.Ok();
            });
            app.MapPost("/body", async (HttpContext context) => { await context.Request.Body.CopyToAsync(Stream.Null); return Results.Ok(); });
            await app.StartAsync(TestContext.Current.CancellationToken);
            var client = testServer ? app.GetTestClient() : new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            return new StandardsHost(app, client, logs)
            { PartialRelease = partialRelease, PartialCompleted = partialCompleted, LogCompleted = logCompleted, CancelStarted = cancelStarted, CancelObserved = cancelObserved, CancelCompleted = cancelCompleted };
        }

        private static IResult Throw() => throw new InvalidOperationException("private-exception-message");
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = "Test";
            return Task.CompletedTask;
        }
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            return Task.CompletedTask;
        }
    }

    private sealed record Payload(string Name);
    private sealed record BoundedRequest(
        [property: MaxLength(ApiStandards.MaxNameLength)] string Name,
        [property: MaxLength(ApiStandards.MaxEquipmentLength)] string Equipment,
        [property: MaxLength(ApiStandards.MaxNotesLength)] string Notes,
        [property: MaxLength(ApiStandards.MaxExercisesPerSession)] int[] Exercises,
        [property: MaxLength(ApiStandards.MaxSetsPerExercise)] int[] Sets);

    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthJsonContent(string json) { bytes = Encoding.UTF8.GetBytes(json); Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private sealed class SizedContent(int size, bool chunked) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = size; return !chunked; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[size]).AsTask();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public Action<LogEntry>? OnEntry { get; set; }
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }
        private sealed class CaptureLogger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(x => x.Key, x => x.Value) : new Dictionary<string, object?>();
                var entry = new LogEntry(properties, $"{category} {formatter(state, exception)} {exception}");
                owner.Entries.Enqueue(entry);
                owner.OnEntry?.Invoke(entry);
            }
        }
    }

    private sealed record LogEntry(Dictionary<string, object?> Properties, string Text);
}
