namespace MuscleForge.Core.Persistence;

public interface IDatabaseProbe
{
    /// <summary>Checks whether the database is reachable and has the required application schema.</summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
