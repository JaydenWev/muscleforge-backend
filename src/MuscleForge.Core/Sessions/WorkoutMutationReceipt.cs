using MuscleForge.Core.Workouts;

namespace MuscleForge.Core.Sessions;

public sealed class WorkoutMutationReceipt
{
    private WorkoutMutationReceipt()
    {
    }
    public WorkoutMutationReceipt(Guid ownerId, Guid mutationId, Guid sessionId, string semanticRequestHash, int resultVersion, string acknowledgement, DateTimeOffset createdAt)
    {
        OwnerId = WorkoutRules.Id(ownerId, nameof(ownerId));
        MutationId = WorkoutRules.Id(mutationId, nameof(mutationId));
        SessionId = WorkoutRules.Id(sessionId, nameof(sessionId));
        SemanticRequestHash = WorkoutRules.Hash(semanticRequestHash);
        if (resultVersion < 1) throw new ArgumentOutOfRangeException(nameof(resultVersion));
        ResultVersion = resultVersion;
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (acknowledgement.Length > 262144) throw new ArgumentException("Acknowledgement is too long.", nameof(acknowledgement));
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(acknowledgement);
            if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new ArgumentException("Acknowledgement must be a JSON object.", nameof(acknowledgement));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException("Acknowledgement must be a JSON object.", nameof(acknowledgement), ex);
        }
        Acknowledgement = acknowledgement;
        CreatedAt = WorkoutRules.Utc(createdAt);
    }
    public Guid OwnerId { get; private set; }
    public Guid MutationId { get; private set; }
    public Guid SessionId { get; private set; }
    public string SemanticRequestHash { get; private set; } = string.Empty;
    public int ResultVersion { get; private set; }
    public string Acknowledgement { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
