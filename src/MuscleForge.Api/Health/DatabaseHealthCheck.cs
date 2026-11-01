using Microsoft.Extensions.Diagnostics.HealthChecks;
using MuscleForge.Core.Persistence;

namespace MuscleForge.Api.Health;

public sealed class DatabaseHealthCheck(IDatabaseProbe database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
