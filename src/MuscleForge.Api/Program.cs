using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MuscleForge.Api.Administration;
using MuscleForge.Api.Contracts;
using MuscleForge.Api.Health;
using MuscleForge.Core.Persistence;
using MuscleForge.Infrastructure.Persistence;

if (args is ["owner", ..])
{
    Environment.ExitCode = await OwnerAdministration.RunAsync(args[1..]);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
    {
        404 => "resource_not_found",
        _ => "request_failed"
    };
});
builder.Services.AddOpenApi("v1", options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "MuscleForge API";
        document.Info.Version = "0.1.0-dev";
        document.Servers = [];
        return Task.CompletedTask;
    });
});

builder.Services.AddDbContext<MuscleForgeDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Database")));
builder.Services.AddScoped<IDatabaseProbe, PostgresDatabaseProbe>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Database")))
{
    throw new InvalidOperationException("Configure ConnectionStrings:Database before starting the API.");
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Production HTTPS terminates at the reverse proxy. Local HTTPS uses the launch profile.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapGet("/api/v1/status", () => TypedResults.Ok(new ApiStatusResponse
{
    Service = "MuscleForge",
    ApiVersion = "v1"
})).WithName("GetApiStatus").WithTags("System");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program { }
