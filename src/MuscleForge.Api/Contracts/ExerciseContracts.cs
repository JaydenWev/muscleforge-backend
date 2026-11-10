using System.ComponentModel.DataAnnotations;

namespace MuscleForge.Api.Contracts;

public class CreateExerciseRequest : IValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    public string? Name { get; init; }
    [Required(ErrorMessage = "Equipment is required.")]
    public string? Equipment { get; init; }
    [Required(ErrorMessage = "Tracking type is required.")]
    [AllowedValues("ExternalWeightAndReps", ErrorMessage = "Tracking type must be ExternalWeightAndReps.")]
    public string? TrackingType { get; init; } = "ExternalWeightAndReps";
    [Required(ErrorMessage = "Load convention is required.")]
    [AllowedValues("TotalLoad", "PerDumbbell", ErrorMessage = "Load convention must be TotalLoad or PerDumbbell.")]
    public string? LoadConvention { get; init; } = "TotalLoad";

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100)
            yield return new ValidationResult("Name is required and must be at most 100 characters after trimming.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Equipment) || Equipment.Trim().Length > 100)
            yield return new ValidationResult("Equipment is required and must be at most 100 characters after trimming.", [nameof(Equipment)]);
        if (TrackingType != "ExternalWeightAndReps")
            yield return new ValidationResult("Tracking type must be ExternalWeightAndReps.", [nameof(TrackingType)]);
        if (LoadConvention is not ("TotalLoad" or "PerDumbbell"))
            yield return new ValidationResult("Load convention must be TotalLoad or PerDumbbell.", [nameof(LoadConvention)]);
    }
}

public sealed class UpdateExerciseRequest : CreateExerciseRequest
{
    [Required(ErrorMessage = "Archive state is required.")]
    public bool? IsArchived { get; init; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext)) yield return result;
        if (IsArchived is null)
            yield return new ValidationResult("Archive state is required.", [nameof(IsArchived)]);
    }
}

public sealed record ExerciseResponse(Guid Id, string Name, string Equipment, string TrackingType,
    string LoadConvention, bool IsArchived, DateTimeOffset? ArchivedAt);
public sealed record ExercisePageResponse(IReadOnlyList<ExerciseResponse> Items, int Offset, int Limit, int TotalCount);
