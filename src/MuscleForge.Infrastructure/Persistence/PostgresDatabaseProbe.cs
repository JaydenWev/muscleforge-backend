using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Persistence;

namespace MuscleForge.Infrastructure.Persistence;

public sealed class PostgresDatabaseProbe(MuscleForgeDbContext database) : IDatabaseProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                return false;
            }

            var expectedMigrations = database.Database.GetMigrations().ToArray();
            if (expectedMigrations.Length == 0)
            {
                return false;
            }

            var appliedMigrations = await database.Database.GetAppliedMigrationsAsync(cancellationToken);
            // Reject both pending migrations and history from an incompatible/newer deployment.
            return expectedMigrations.SequenceEqual(appliedMigrations, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
