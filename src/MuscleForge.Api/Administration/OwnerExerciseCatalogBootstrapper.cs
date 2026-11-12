using Microsoft.AspNetCore.Identity;
using MuscleForge.Infrastructure.Exercises;
using MuscleForge.Infrastructure.Identity;

namespace MuscleForge.Api.Administration;

// Called only after configured account provisioning succeeds. Schema creation and
// account creation stay separate from the repeatable exercise seed operation.
public static class OwnerExerciseCatalogBootstrapper
{
    public const string FailureMessage =
        "Owner exercise catalog initialization failed. Check database connectivity and applied migrations.";

    public static async Task<int> EnsureSeededAsync(IServiceProvider services, string ownerEmail,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await users.FindByEmailAsync(ownerEmail);
            if (owner is null) throw new InvalidOperationException();
            return await scope.ServiceProvider.GetRequiredService<ExerciseCatalogSeeder>()
                .SeedAsync(owner.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Startup logs must not expose database credentials or inner diagnostics.
            throw new InvalidOperationException(FailureMessage);
        }
    }
}
