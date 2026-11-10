namespace MuscleForge.Core.Exercises;

public sealed class Exercise
{
    public const int MaximumNameLength = 100;
    public const int MaximumEquipmentLength = 100;
    public const int MaximumSeedKeyLength = 64;

    private Exercise() { }

    public Exercise(Guid ownerId, string name, string equipment,
        ExerciseTrackingType trackingType = ExerciseTrackingType.ExternalWeightAndReps,
        ExerciseLoadConvention loadConvention = ExerciseLoadConvention.TotalLoad)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An exercise requires an owner.", nameof(ownerId));
        }

        if (trackingType != ExerciseTrackingType.ExternalWeightAndReps)
        {
            throw new ArgumentOutOfRangeException(nameof(trackingType), trackingType,
                "Only external weight and repetitions are supported.");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        TrackingType = trackingType;
        Update(name, equipment, loadConvention);
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Equipment { get; private set; } = string.Empty;
    public ExerciseTrackingType TrackingType { get; private set; }
    public ExerciseLoadConvention LoadConvention { get; private set; }
    public string? SeedKey { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public void Rename(string name, string equipment)
    {
        Update(name, equipment, LoadConvention);
    }

    public void Update(string name, string equipment, ExerciseLoadConvention loadConvention)
    {
        if (loadConvention is not ExerciseLoadConvention.TotalLoad and not ExerciseLoadConvention.PerDumbbell)
        {
            throw new ArgumentOutOfRangeException(nameof(loadConvention));
        }

        // Validate every value before changing any field.
        var validName = Validate(name, MaximumNameLength, nameof(name));
        var validEquipment = Validate(equipment, MaximumEquipmentLength, nameof(equipment));
        Name = validName;
        Equipment = validEquipment;
        LoadConvention = loadConvention;
    }

    public void Archive(DateTimeOffset timestamp) => ArchivedAt ??= timestamp.ToUniversalTime();

    private static string Validate(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new ArgumentException($"Must contain at most {maximumLength} characters.", parameterName);
        }

        return trimmed;
    }
}
