using MuscleForge.Core.Measurements;

namespace MuscleForge.Core.Workouts;

public enum SetKind { Warmup, Working }

public static class WorkoutRules
{
    public const int MaximumExercises = 100;
    public const int MaximumSets = 100;
    public const int MaximumNotesLength = 2000;
    public const int MaximumNameLength = 100;

    public static Guid Id(Guid id, string name) => id != Guid.Empty
        ? id
        : throw new ArgumentException("An ID is required.", name);

    public static int Position(int position) => position is >= 0 and < 100
        ? position
        : throw new ArgumentOutOfRangeException(nameof(position));

    public static SetKind Kind(SetKind kind) => kind is SetKind.Warmup or SetKind.Working
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(kind));

    public static string Name(string name) => Required(name, MaximumNameLength, nameof(name));

    public static string Required(string value, int max, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        return value.Length <= max ? value : throw new ArgumentException($"Must contain at most {max} characters.", parameter);
    }

    public static string? Notes(string? notes) => notes is null || notes.Length <= MaximumNotesLength
        ? notes
        : throw new ArgumentException("Notes are too long.", nameof(notes));

    public static decimal? Weight(decimal? value) => value is null ? null : ExternalWeight.ToKilograms(value.Value, WeightUnit.Kilograms);

    public static int? Reps(int? value) => value is null or > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value));

    public static int? Rest(int? seconds) => seconds is null or >= 0 and <= 86400
        ? seconds
        : throw new ArgumentOutOfRangeException(nameof(seconds));

    public static void RepRange(int? min, int? max)
    {
        Reps(min);
        Reps(max);
        if ((min is null) != (max is null) || min > max) throw new ArgumentException("Repetition range must have positive ordered endpoints or be empty.");
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static string Hash(string hash) => hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        ? hash
        : throw new ArgumentException("A lowercase SHA-256 hex digest is required.", nameof(hash));
}
