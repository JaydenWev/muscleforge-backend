using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Shared by environment bootstrap, maintenance and bearer sign-in.
    public static IdentityBuilder AddMuscleForgeIdentity(this IServiceCollection services) =>
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        }).AddEntityFrameworkStores<MuscleForgeDbContext>();
}
