namespace MuscleForge.Core.Exercises;

public sealed class Exercise
{
    public const int MaximumNameLength = 100;
    public const int MaximumEquipmentLength = 100;

    private Exercise() { }

    public Exercise(Guid ownerId, string name, string equipment)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An exercise requires an owner.", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Rename(name, equipment);
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Equipment { get; private set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; private set; }

    public void Rename(string name, string equipment)
    {
        // Validate both values before changing either field.
        var validName = Validate(name, MaximumNameLength, nameof(name));
        var validEquipment = Validate(equipment, MaximumEquipmentLength, nameof(equipment));
        Name = validName;
        Equipment = validEquipment;
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
