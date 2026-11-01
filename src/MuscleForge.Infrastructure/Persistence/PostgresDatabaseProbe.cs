using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Persistence;

namespace MuscleForge.Infrastructure.Persistence;

public sealed class PostgresDatabaseProbe(MuscleForgeDbContext database) : IDatabaseProbe
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        database.Database.CanConnectAsync(cancellationToken);
}
