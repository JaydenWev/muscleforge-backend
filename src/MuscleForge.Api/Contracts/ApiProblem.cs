using System.Text.Json.Serialization;

namespace MuscleForge.Api.Contracts;

public class ApiProblem
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public required int Status { get; init; }
    public required string Code { get; init; }
    public required string TraceId { get; init; }
}

public sealed class ApiValidationProblem : ApiProblem
{
    public required IDictionary<string, string[]> Errors { get; init; }
}
