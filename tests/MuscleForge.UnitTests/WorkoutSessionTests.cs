using MuscleForge.Core.Exercises;
using MuscleForge.Core.Sessions;
using MuscleForge.Core.Workouts;
using MuscleForge.Core.Templates;
using Xunit;

namespace MuscleForge.UnitTests;

public sealed class WorkoutSessionTests
{
    [Fact]
    public void SessionSnapshotsExerciseAndPrescriptionAndEnforcesTerminalState()
    {
        var owner = Guid.NewGuid(); var at = DateTimeOffset.UtcNow; var exercise = new Exercise(owner, "Curl", "Dumbbell", loadConvention: ExerciseLoadConvention.PerDumbbell);
        var session = new WorkoutSession(Guid.NewGuid(), owner, at, "Etc/UTC", new string('a', 64));
        var childId = Guid.NewGuid(); var setId = Guid.NewGuid(); session.AddExercise(childId, exercise, 0); session.AddSet(childId, setId, 0, SetKind.Working, 10m, 8, 12);
        exercise.Update("Renamed", "Barbell", ExerciseLoadConvention.TotalLoad);
        var child = Assert.Single(session.Exercises); var set = Assert.Single(child.Sets);
        Assert.Equal("Curl", child.ExerciseName); Assert.Equal(ExerciseLoadConvention.PerDumbbell, child.LoadConvention);
        Assert.Throws<InvalidOperationException>(() => session.Complete(at));
        Assert.Throws<ArgumentException>(() => session.UpdateSet(childId, setId, SetKind.Working, null, 5, true, null));
        session.UpdateSet(childId, setId, SetKind.Warmup, 0m, 5, true, null);
        Assert.Throws<InvalidOperationException>(() => session.Complete(at));
        session.UpdateSet(childId, setId, SetKind.Working, 12.5m, 5, true, null);
        Assert.Equal(10m, set.TargetWeightKg); Assert.Equal(8, set.TargetRepsMin); Assert.Equal(SetKind.Working, set.TargetKind);
        session.AdvanceVersion(); session.Complete(at); Assert.Equal(2, session.Version);
        Assert.Throws<InvalidOperationException>(() => session.UpdateSet(childId, setId, SetKind.Working, 1m, 1, true, null));
        Assert.Throws<InvalidOperationException>(() => session.Discard(at));
        Assert.Throws<InvalidOperationException>(() => session.AdvanceVersion());
    }
    [Fact]
    public void TemplateCopyPreservesProvenanceAndPrescriptions()
    {
        var owner = Guid.NewGuid();
        var exercise = new Exercise(owner, "Curl", "Dumbbell", loadConvention: ExerciseLoadConvention.PerDumbbell);
        var template = new WorkoutTemplate(owner, "Strength");
        var source = template.AddExercise(exercise, 0, 90);
        template.AddSet(source.Id, 0, SetKind.Working, 12m, 6, 10);
        var sourceSet = Assert.Single(source.Sets);
        var childId = Guid.NewGuid(); var setId = Guid.NewGuid();
        var session = WorkoutSession.FromTemplate(Guid.NewGuid(), owner, DateTimeOffset.UtcNow, "Etc/UTC", new string('a', 64), template,
            new Dictionary<Guid, Exercise> { [exercise.Id] = exercise }, new Dictionary<Guid, Guid> { [source.Id] = childId }, new Dictionary<Guid, Guid> { [sourceSet.Id] = setId });
        template.Rename("Changed"); exercise.Update("Changed", "Barbell", ExerciseLoadConvention.TotalLoad);
        var copied = Assert.Single(session.Exercises); var set = Assert.Single(copied.Sets);
        Assert.Equal("Strength", session.TemplateName); Assert.Equal(1, session.SourceTemplateVersion);
        Assert.Equal("Curl", copied.ExerciseName); Assert.Equal(90, copied.RestSeconds); Assert.Equal(ExerciseLoadConvention.PerDumbbell, copied.LoadConvention);
        Assert.Equal(12m, set.TargetWeightKg); Assert.Equal(6, set.TargetRepsMin); Assert.Equal(10, set.TargetRepsMax); Assert.Equal(SetKind.Working, set.TargetKind);
    }
    [Fact]
    public void SessionRejectsInvalidStartIdentityAndCrossOwnerExercise()
    {
        var owner = Guid.NewGuid(); var at = DateTimeOffset.UtcNow; var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => new WorkoutSession(Guid.NewGuid(), owner, at, "Etc/UTC", hash, Guid.NewGuid(), "Template"));
        Assert.Throws<ArgumentException>(() => new WorkoutSession(Guid.NewGuid(), owner, at, "invalid/zone", hash));
        var session = new WorkoutSession(Guid.NewGuid(), owner, at, "Etc/UTC", hash);
        Assert.Throws<ArgumentException>(() => session.AddExercise(Guid.NewGuid(), new Exercise(Guid.NewGuid(), "Squat", "Barbell"), 0));
    }
}
