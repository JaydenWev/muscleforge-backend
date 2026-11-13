using Microsoft.AspNetCore.Identity;

namespace MuscleForge.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string WeightUnit { get; set; } = "kg";
    public string TimeZoneId { get; set; } = "Etc/UTC";
}
