using System.ComponentModel.DataAnnotations;
using MuscleForge.Core.Time;

namespace MuscleForge.Api.Contracts;

public sealed class UpdateAccountPreferencesRequest : IValidatableObject
{
    [Required(ErrorMessage = "Weight unit is required.")]
    [AllowedValues("kg", "lb", ErrorMessage = "Weight unit must be kg or lb.")]
    public string? WeightUnit { get; init; }

    [Required(ErrorMessage = "Time zone is required.")]
    [StringLength(100, ErrorMessage = "Time zone must be at most 100 characters.")]
    public string? TimeZoneId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WeightUnit is not ("kg" or "lb"))
            yield return new ValidationResult("Weight unit must be kg or lb.", [nameof(WeightUnit)]);
        if (TimeZoneId is null || TimeZoneId.Length > 100 || !SessionTimeZone.IsValid(TimeZoneId))
            yield return new ValidationResult("A known IANA time zone is required.", [nameof(TimeZoneId)]);
    }
}

public sealed record AccountPreferencesResponse(string WeightUnit, string TimeZoneId);
