using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace MuscleForge.Api.Standards;

public static class ApiStandards
{
    public const long MaxRequestBodyBytes = 256 * 1024;
    // Kestrel counts HTTP/1 chunk framing as well as decoded body bytes.
    // Keep transport bounded while the stream enforces the decoded body limit.
    public const long MaxTransportBodyBytes = 2 * 1024 * 1024;
    public const int MaxNameLength = MuscleForge.Core.Exercises.Exercise.MaximumNameLength;
    public const int MaxEquipmentLength = MuscleForge.Core.Exercises.Exercise.MaximumEquipmentLength;
    public const int MaxNotesLength = 2000;
    public const int MaxExercisesPerSession = 50;
    public const int MaxSetsPerExercise = 50;
    internal static readonly object ErrorCodeKey = new();

    public static IServiceCollection AddApiStandards(this IServiceCollection services)
    {
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }

    public static IApplicationBuilder UseApiStandards(this IApplicationBuilder app) => app.UseMiddleware<ApiStandardsMiddleware>();
}

internal sealed class ApiStandardsMiddleware(RequestDelegate next, ILogger<ApiStandardsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
            bodySizeFeature.MaxRequestBodySize = ApiStandards.MaxTransportBodyBytes;
        var originalBody = context.Request.Body;
        context.Request.Body = new LimitedRequestStream(originalBody, ApiStandards.MaxRequestBodyBytes);
        try
        {
            if (context.Request.ContentLength > ApiStandards.MaxRequestBodyBytes)
                await ApiErrors.WriteAsync(context, StatusCodes.Status413PayloadTooLarge);
            else
            {
                await next(context);
                if (!context.Response.HasStarted && context.Response.StatusCode >= 400 && (context.Response.ContentLength is null or 0)
                    && context.Response.ContentType is null && !IsHealth(context))
                    await ApiErrors.WriteAsync(context, context.Response.StatusCode);
            }
        }
        catch (Exception exception) when (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException or IOException)
        {
            context.Items[ApiStandards.ErrorCodeKey] = context.Response.HasStarted ? "response_aborted" : "request_cancelled";
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
            context.Abort();
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                context.Items[ApiStandards.ErrorCodeKey] = "response_aborted";
                context.Abort();
            }
            else
            {
                var status = exception switch
                {
                    RequestBodyTooLargeException => 413,
                    BadHttpRequestException bad when bad.StatusCode is >= 400 and < 500 => bad.StatusCode,
                    _ => 500
                };
                context.Response.Clear();
                try
                {
                    await ApiErrors.WriteAsync(context, status);
                }
                catch (Exception) when (context.RequestAborted.IsCancellationRequested)
                {
                    context.Items[ApiStandards.ErrorCodeKey] = context.Response.HasStarted ? "response_aborted" : "request_cancelled";
                    if (!context.Response.HasStarted)
                        context.Response.StatusCode = 499;
                    context.Abort();
                }
                catch (Exception)
                {
                    context.Items[ApiStandards.ErrorCodeKey] = "response_aborted";
                    context.Abort();
                }
            }
        }
        finally
        {
            context.Request.Body = originalBody;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            logger.LogInformation("HTTP {Route} returned {StatusCode} in {DurationMs} ms; trace {TraceId}; error {ErrorCode}",
                route, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                context.TraceIdentifier, context.Items[ApiStandards.ErrorCodeKey]);
        }
    }

    private static bool IsHealth(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions>() is not null;
}
