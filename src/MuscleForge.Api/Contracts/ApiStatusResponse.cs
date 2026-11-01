namespace MuscleForge.Api.Contracts;

public sealed class ApiStatusResponse
{
    public required string Service { get; init; }
    public required string ApiVersion { get; init; }
}
