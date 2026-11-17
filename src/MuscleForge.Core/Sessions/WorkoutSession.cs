using MuscleForge.Core.Exercises;
using MuscleForge.Core.Time;
using MuscleForge.Core.Templates;
using MuscleForge.Core.Workouts;

namespace MuscleForge.Core.Sessions;

public enum WorkoutSessionStatus { InProgress, Completed, Discarded }

public sealed class WorkoutSession
{
    private readonly List<SessionExercise> _exercises = [];
    private WorkoutSession()
    {
    }
    public WorkoutSession(Guid id, Guid ownerId, DateTimeOffset startedAt, string timeZoneId, string startRequestHash, Guid? templateId = null, string? templateName = null, int? sourceTemplateVersion = null)
    {
        Id = WorkoutRules.Id(id, nameof(id));
        OwnerId = WorkoutRules.Id(ownerId, nameof(ownerId));
        if (templateId == Guid.Empty || (templateId is null) != (templateName is null) || (templateId is null) != (sourceTemplateVersion is null) || sourceTemplateVersion is <= 0) throw new ArgumentException("Template ID and name must be provided together.");
        TemplateId = templateId;
        SourceTemplateVersion = sourceTemplateVersion;
        TemplateName = templateName is null ? null : WorkoutRules.Name(templateName);
        ArgumentNullException.ThrowIfNull(timeZoneId);
        if (timeZoneId.Length > 100) throw new ArgumentException("Time zone is too long.", nameof(timeZoneId));
        SessionTimeZone.Resolve(timeZoneId);
        TimeZoneId = timeZoneId;
        StartedAt = WorkoutRules.Utc(startedAt);
        StartRequestHash = WorkoutRules.Hash(startRequestHash);
        Version = 1;
    }
    public static WorkoutSession FromTemplate(Guid id, Guid ownerId, DateTimeOffset startedAt, string timeZoneId,
        string startRequestHash, WorkoutTemplate template, IReadOnlyDictionary<Guid, Exercise> exercises,
        IReadOnlyDictionary<Guid, Guid> exerciseIds, IReadOnlyDictionary<Guid, Guid> setIds)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(exercises);
        ArgumentNullException.ThrowIfNull(exerciseIds);
        ArgumentNullException.ThrowIfNull(setIds);
        if (template.OwnerId != ownerId || template.ArchivedAt is not null)
            throw new ArgumentException("Template must be active and owned by the session owner.", nameof(template));
        var session = new WorkoutSession(id, ownerId, startedAt, timeZoneId, startRequestHash, template.Id, template.Name, template.Version);
        foreach (var item in template.Exercises)
        {
            if (!exercises.TryGetValue(item.ExerciseId, out var exercise) || exercise.Id != item.ExerciseId || !exerciseIds.TryGetValue(item.Id, out var childId))
                throw new ArgumentException("Every template exercise requires its owned exercise and a session ID.");
            session.AddExercise(childId, exercise, item.Position, item.RestSeconds);
            foreach (var set in item.Sets)
            {
                if (!setIds.TryGetValue(set.Id, out var setId)) throw new ArgumentException("Every template set requires a session ID.");
                session.AddSet(childId, setId, set.Position, set.Kind, set.TargetWeightKg, set.TargetRepsMin, set.TargetRepsMax);
            }
        }
        return session;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? TemplateId { get; private set; }
    public string? TemplateName { get; private set; }
    public int? SourceTemplateVersion { get; private set; }
    public string StartRequestHash { get; private set; } = string.Empty;
    public WorkoutSessionStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public IReadOnlyCollection<SessionExercise> Exercises => _exercises.AsReadOnly();
    public void SetNotes(string? notes)
    {
        EnsureActive();
        Notes = WorkoutRules.Notes(notes);
    }
    public void AddExercise(Guid id, Exercise exercise, int position, int? restSeconds = null)
    {
        EnsureActive();
        WorkoutRules.Id(id, nameof(id));
        ArgumentNullException.ThrowIfNull(exercise);
        WorkoutRules.Position(position);
        WorkoutRules.Rest(restSeconds);
        if (exercise.OwnerId != OwnerId || exercise.ArchivedAt is not null) throw new ArgumentException("Exercise must be active and owned by the session owner.", nameof(exercise));
        if (_exercises.Count >= WorkoutRules.MaximumExercises || _exercises.Any(x => x.Position == position || x.Id == id)) throw new InvalidOperationException("Exercise limit or duplicate ID or position.");
        _exercises.Add(new SessionExercise(id, OwnerId, Id, exercise, position, restSeconds));
    }
    public void AddSet(Guid exerciseId, Guid setId, int position, SetKind kind, decimal? targetWeightKg = null, int? targetRepsMin = null, int? targetRepsMax = null)
    {
        EnsureActive();
        _exercises.Single(x => x.Id == exerciseId).AddSet(setId, position, kind, targetWeightKg, targetRepsMin, targetRepsMax);
    }
    public void UpdateSet(Guid exerciseId, Guid setId, SetKind kind, decimal? weightKg, int? reps, bool completed, string? notes)
    {
        EnsureActive();
        _exercises.Single(x => x.Id == exerciseId).UpdateSet(setId, kind, weightKg, reps, completed, notes);
    }
    public void Complete(DateTimeOffset at)
    {
        EnsureActive();
        if (!_exercises.SelectMany(x => x.Sets).Any(x => x.Completed && x.Kind == SetKind.Working)) throw new InvalidOperationException("A completed working set is required.");
        End(at, WorkoutSessionStatus.Completed);
    }
    public void Discard(DateTimeOffset at)
    {
        EnsureActive();
        End(at, WorkoutSessionStatus.Discarded);
    }
    // A request handler advances exactly once for an accepted mutation, before completion/discard.
    public void AdvanceVersion()
    {
        EnsureActive();
        if (Version == int.MaxValue) throw new InvalidOperationException("Version exhausted.");
        Version++;
    }
    private void End(DateTimeOffset at, WorkoutSessionStatus status)
    {
        at = WorkoutRules.Utc(at);
        if (at < StartedAt) throw new ArgumentOutOfRangeException(nameof(at));
        EndedAt = at;
        Status = status;
    }
    private void EnsureActive()
    {
        if (Status != WorkoutSessionStatus.InProgress) throw new InvalidOperationException("A terminal session cannot be edited.");
    }
}

public sealed class SessionExercise
{
    private readonly List<WorkoutSet> _sets = [];
    private SessionExercise()
    {
    }
    internal SessionExercise(Guid id, Guid ownerId, Guid sessionId, Exercise exercise, int position, int? restSeconds)
    {
        RestSeconds = restSeconds;
        Id = id;
        OwnerId = ownerId;
        SessionId = sessionId;
        ExerciseId = exercise.Id;
        Position = position;
        ExerciseName = exercise.Name;
        Equipment = exercise.Equipment;
        TrackingType = exercise.TrackingType;
        LoadConvention = exercise.LoadConvention;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public int Position { get; private set; }
    public int? RestSeconds { get; private set; }
    public string ExerciseName { get; private set; } = string.Empty;
    public string Equipment { get; private set; } = string.Empty;
    public ExerciseTrackingType TrackingType { get; private set; }
    public ExerciseLoadConvention LoadConvention { get; private set; }
    public IReadOnlyCollection<WorkoutSet> Sets => _sets.AsReadOnly();
    internal void AddSet(Guid id, int position, SetKind kind, decimal? targetWeight, int? targetRepsMin, int? targetRepsMax)
    {
        WorkoutRules.Id(id, nameof(id));
        WorkoutRules.Position(position);
        WorkoutRules.Kind(kind);
        targetWeight = WorkoutRules.Weight(targetWeight);
        WorkoutRules.RepRange(targetRepsMin, targetRepsMax);
        if (_sets.Count >= WorkoutRules.MaximumSets || _sets.Any(x => x.Id == id || x.Position == position)) throw new InvalidOperationException("Set limit or duplicate ID or position.");
        _sets.Add(new WorkoutSet(id, OwnerId, Id, position, kind, targetWeight, targetRepsMin, targetRepsMax));
    }
    internal void UpdateSet(Guid id, SetKind kind, decimal? weight, int? reps, bool completed, string? notes) => _sets.Single(x => x.Id == id).Update(kind, weight, reps, completed, notes);
}

public sealed class WorkoutSet
{
    private WorkoutSet()
    {
    }
    internal WorkoutSet(Guid id, Guid ownerId, Guid exerciseId, int position, SetKind kind, decimal? targetWeight, int? targetRepsMin, int? targetRepsMax)
    {
        Id = id;
        OwnerId = ownerId;
        SessionExerciseId = exerciseId;
        Position = position;
        Kind = kind;
        TargetKind = kind;
        TargetWeightKg = targetWeight;
        TargetRepsMin = targetRepsMin;
        TargetRepsMax = targetRepsMax;
    }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SessionExerciseId { get; private set; }
    public int Position { get; private set; }
    public SetKind Kind { get; private set; }
    public SetKind TargetKind { get; private set; }
    public decimal? TargetWeightKg { get; private set; }
    public int? TargetRepsMin { get; private set; }
    public int? TargetRepsMax { get; private set; }
    public decimal? WeightKg { get; private set; }
    public int? Reps { get; private set; }
    public bool Completed { get; private set; }
    public string? Notes { get; private set; }
    internal void Update(SetKind kind, decimal? weight, int? reps, bool completed, string? notes)
    {
        WorkoutRules.Kind(kind);
        weight = WorkoutRules.Weight(weight);
        reps = WorkoutRules.Reps(reps);
        notes = WorkoutRules.Notes(notes);
        if (completed && (weight is null || reps is null)) throw new ArgumentException("Completed sets require weight and repetitions.");
        Kind = kind;
        WeightKg = weight;
        Reps = reps;
        Completed = completed;
        Notes = notes;
    }
}

