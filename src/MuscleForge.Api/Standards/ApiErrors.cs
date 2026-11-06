using System.Text.Json;
using MuscleForge.Api.Contracts;

namespace MuscleForge.Api.Standards;

// Callers supply only fixed, public validation messages; never echo submitted values.
public static class ApiErrors
{
    public static IResult Validation(IDictionary<string, string[]> errors) => new ProblemResult(400, "validation_failed", errors);
    public static IResult VersionConflict() => new ProblemResult(409, "version_conflict");
    public static IResult StateConflict() => new ProblemResult(409, "state_conflict");
    public static IResult ForStatus(int status) => status is >= 400 and <= 599
        ? new ProblemResult(status)
        : throw new ArgumentOutOfRangeException(nameof(status));

    internal static (string Code, string Title) Describe(int status) => status switch
    {
        400 => ("invalid_request", "Invalid request"),
        401 => ("authentication_required", "Authentication required"),
        403 => ("access_denied", "Access denied"),
        404 => ("resource_not_found", "Resource not found"),
        405 => ("method_not_allowed", "Method not allowed"),
        409 => ("conflict", "Conflict"),
        413 => ("request_too_large", "Request too large"),
        415 => ("unsupported_media_type", "Unsupported media type"),
        429 => ("rate_limited", "Too many requests"),
        500 => ("internal_error", "Internal server error"),
        503 => ("service_unavailable", "Service unavailable"),
        _ => ("request_failed", "Request failed")
    };

    internal static async Task WriteAsync(HttpContext context, int status, string? code = null, IDictionary<string, string[]>? errors = null)
    {
        var description = Describe(status);
        code ??= description.Code;
        context.Items[ApiStandards.ErrorCodeKey] = code;
        context.Response.StatusCode = status;
        context.Response.ContentLength = null;
        context.Response.ContentType = "application/problem+json";
        ApiProblem problem = errors is null
            ? new ApiProblem { Type = "about:blank", Title = description.Title, Status = status, Code = code, TraceId = context.TraceIdentifier }
            : new ApiValidationProblem { Type = "about:blank", Title = "Validation failed", Status = status, Code = code, TraceId = context.TraceIdentifier, Errors = errors };
        if (problem is ApiValidationProblem validationProblem)
            await context.Response.WriteAsJsonAsync(validationProblem, options: (JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        else
            await context.Response.WriteAsJsonAsync(problem, options: (JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }

    private sealed class ProblemResult(int status, string? code = null, IDictionary<string, string[]>? errors = null) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => WriteAsync(httpContext, status, code, errors);
    }
}
