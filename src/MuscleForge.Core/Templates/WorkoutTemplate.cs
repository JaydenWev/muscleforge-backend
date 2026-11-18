using MuscleForge.Core.Exercises;
using MuscleForge.Core.Workouts;

namespace MuscleForge.Core.Templates;

public sealed class WorkoutTemplate
{
    private readonly List<TemplateExercise> _exercises = [];
    private WorkoutTemplate()
    {
    }
    public WorkoutTemplate(Guid ownerId, string name)
    {
        Id = Guid.NewGuid();
        OwnerId = WorkoutRules.Id(ownerId, nameof(ownerId));
        Name = WorkoutRules.Name(name);
        Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public IReadOnlyCollection<TemplateExercise> Exercises => _exercises.AsReadOnly();
    public void AdvanceVersion()
    {
        EnsureEditable();
        if (Version == int.MaxValue) throw new InvalidOperationException("Version exhausted.");
        Version++;
    }
    public void Rename(string name)
    {
        EnsureEditable();
        Name = WorkoutRules.Name(name);
    }
    public void Archive(DateTimeOffset at) => ArchivedAt ??= WorkoutRules.Utc(at);
    public TemplateExercise AddExercise(Exercise exercise, int position, int? restSeconds = null)
    {
        EnsureEditable();
        ArgumentNullException.ThrowIfNull(exercise);
        if (exercise.OwnerId != OwnerId || exercise.ArchivedAt is not null) throw new ArgumentException("Exercise must be active and owned by the template owner.", nameof(exercise));
        WorkoutRules.Position(position);
        WorkoutRules.Rest(restSeconds);
        if (_exercises.Count >= WorkoutRules.MaximumExercises || _exercises.Any(x => x.Position == position)) throw new InvalidOperationException("Exercise limit or duplicate position.");
        var item = new TemplateExercise(OwnerId, Id, exercise.Id, position, restSeconds);
        _exercises.Add(item);
        return item;
    }
    public void AddSet(Guid templateExerciseId, int position, SetKind kind, decimal? targetWeightKg, int? targetRepsMin, int? targetRepsMax)
    {
        EnsureEditable();
        var exercise = _exercises.Single(x => x.Id == templateExerciseId);
        exercise.AddSet(position, kind, targetWeightKg, targetRepsMin, targetRepsMax);
    }
    private void EnsureEditable()
    {
        if (ArchivedAt is not null) throw new InvalidOperationException("Archived templates cannot be edited.");
    }
}

public sealed class TemplateExercise
{
    private readonly List<TemplateSet> _sets = [];
    private TemplateExercise()
    {
    }
    internal TemplateExercise(Guid ownerId, Guid templateId, Guid exerciseId, int position, int? restSeconds)
    {
        RestSeconds = restSeconds;
        Id = Guid.NewGuid();
        OwnerId = ownerId;
        TemplateId = templateId;
        ExerciseId = exerciseId;
        Position = position;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public int Position { get; private set; }
    public int? RestSeconds { get; private set; }
    public IReadOnlyCollection<TemplateSet> Sets => _sets.AsReadOnly();
    internal void AddSet(int position, SetKind kind, decimal? weight, int? min, int? max)
    {
        WorkoutRules.Position(position);
        WorkoutRules.Kind(kind);
        weight = WorkoutRules.Weight(weight);
        WorkoutRules.RepRange(min, max);
        if (_sets.Count >= WorkoutRules.MaximumSets || _sets.Any(x => x.Position == position)) throw new InvalidOperationException("Set limit or duplicate position.");
        _sets.Add(new TemplateSet(OwnerId, Id, position, kind, weight, min, max));
    }
}

public sealed class TemplateSet
{
    private TemplateSet()
    {
    }
    internal TemplateSet(Guid ownerId, Guid exerciseId, int position, SetKind kind, decimal? weight, int? min, int? max)
    {
        Id = Guid.NewGuid();
        OwnerId = ownerId;
        TemplateExerciseId = exerciseId;
        Position = position;
        Kind = kind;
        TargetWeightKg = weight;
        TargetRepsMin = min;
        TargetRepsMax = max;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid TemplateExerciseId { get; private set; }
    public int Position { get; private set; }
    public SetKind Kind { get; private set; }
    public decimal? TargetWeightKg { get; private set; }
    public int? TargetRepsMin { get; private set; }
    public int? TargetRepsMax { get; private set; }
}
