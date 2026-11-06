using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MuscleForge.Api.Contracts;

namespace MuscleForge.Api.Standards;

public static class ApiValidation
{
    // Validation messages must be fixed public text and must not include submitted values.
    // Nested objects and collection elements require feature-specific validation.
    public static RouteHandlerBuilder WithApiValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class => builder.AddEndpointFilter(async (context, next) =>
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
            return await next(context);

        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
            return await next(context);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var members = result.MemberNames.DefaultIfEmpty(string.Empty);
            foreach (var member in members)
            {
                var property = typeof(TRequest).GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                var key = property?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(member);
                if (!errors.TryGetValue(key, out var messages))
                    errors[key] = messages = [];
                messages.Add(result.ErrorMessage ?? "The value is invalid.");
            }
        }
        return ApiErrors.Validation(errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
    }).Produces<ApiValidationProblem>(StatusCodes.Status400BadRequest, "application/problem+json");
}
