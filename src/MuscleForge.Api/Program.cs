using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MuscleForge.Api.Authentication;
using MuscleForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MuscleForge.Api.Administration;
using MuscleForge.Api.Contracts;
using MuscleForge.Api.Health;
using MuscleForge.Api.Standards;
using MuscleForge.Core.Persistence;
using MuscleForge.Infrastructure.Persistence;

if (args is ["owner", ..])
{
    Environment.ExitCode = await OwnerAdministration.RunAsync(args[1..]);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ApiStandards.MaxTransportBodyBytes);
builder.Services.AddApiStandards();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddAuthDataProtection();
builder.Services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);
});
builder.Services.AddAuthorization();
builder.Services.AddMuscleForgeIdentity().AddSignInManager();
builder.Services.AddAuthRateLimits();
builder.Services.AddOpenApi("v1", options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "MuscleForge API";
        document.Info.Version = "0.1.0-dev";
        document.Servers = [];
        document.Components ??= new OpenApiComponents();
        // The token response is serialized as a number. The framework schema also
        // describes strings accepted during deserialization, which this output-only
        // type never receives and which generators cannot reliably represent.
        if (document.Components.Schemas?.TryGetValue("AccessTokenResponse", out var tokenSchema) == true
            && tokenSchema is OpenApiSchema { Properties: not null } responseSchema
            && responseSchema.Properties.TryGetValue("expiresIn", out var expiresIn)
            && expiresIn is OpenApiSchema expirySchema)
        {
            expirySchema.Type = JsonSchemaType.Integer;
            expirySchema.Pattern = null;
            expirySchema.AnyOf = null;
        }
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.RelativePath == "api/v1/auth/me")
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
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

app.ValidateAuthDataProtection();

var ownerBootstrap = await OwnerAccountBootstrapper.EnsureCreatedAsync(
    app.Configuration, app.Lifetime.ApplicationStopping);
if (ownerBootstrap == OwnerBootstrapResult.Created)
{
    app.Logger.LogInformation("Owner account created from configured startup credentials.");
}
else if (ownerBootstrap == OwnerBootstrapResult.AlreadyExists)
{
    app.Logger.LogInformation("Configured owner account already exists; no changes made.");
}

app.UseRouting();
app.UseApiStandards();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapAuthEndpoints();

// Production HTTPS terminates at the reverse proxy. Local HTTPS uses the launch profile.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapGet("/api/v1/status", () => TypedResults.Ok(new ApiStatusResponse
{
    Service = "MuscleForge",
    ApiVersion = "v1"
})).WithName("GetApiStatus").WithTags("System")
    .Produces<ApiProblem>(StatusCodes.Status500InternalServerError, "application/problem+json");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program { }
