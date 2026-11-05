using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Reuse this policy when the bearer-only sign-in endpoints are added.
    public static IdentityBuilder AddMuscleForgeIdentity(this IServiceCollection services) =>
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
        }).AddEntityFrameworkStores<MuscleForgeDbContext>();
}
