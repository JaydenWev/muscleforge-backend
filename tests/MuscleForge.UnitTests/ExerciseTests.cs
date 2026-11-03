using MuscleForge.Core.Exercises;
using Xunit;

namespace MuscleForge.UnitTests;

public sealed class ExerciseTests
{
    [Fact]
    public void AnExerciseRequiresAnOwner() =>
        Assert.Throws<ArgumentException>(() => new Exercise(Guid.Empty, "Squat", "Barbell"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003\u202f\u3000")]
    public void AnExerciseRequiresAName(string name) =>
        Assert.Throws<ArgumentException>(() => new Exercise(Guid.NewGuid(), name, "Barbell"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003\u202f\u3000")]
    public void AnExerciseRequiresEquipment(string equipment) =>
        Assert.Throws<ArgumentException>(() => new Exercise(Guid.NewGuid(), "Squat", equipment));

    [Fact]
    public void AnExerciseDefaultsToExternalWeightAndRepetitions()
    {
        var exercise = new Exercise(Guid.NewGuid(), "Squat", "Barbell");
        Assert.Equal(ExerciseTrackingType.ExternalWeightAndReps, exercise.TrackingType);
    }

    [Fact]
    public void UnsupportedTrackingTypesAreRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Exercise(Guid.NewGuid(), "Squat", "Barbell", (ExerciseTrackingType)1));

    [Fact]
    public void FailedRenamePreservesBothPreviousValues()
    {
        var exercise = new Exercise(Guid.NewGuid(), "Squat", "Barbell");
        Assert.Throws<ArgumentException>(() => exercise.Rename("Deadlift", new string('x', 101)));
        Assert.Equal("Squat", exercise.Name);
        Assert.Equal("Barbell", exercise.Equipment);
    }

    [Fact]
    public void ArchivingTwiceRetainsTheOriginalTimestamp()
    {
        var exercise = new Exercise(Guid.NewGuid(), " Squat ", " Barbell ");
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(1));
        exercise.Archive(now);
        exercise.Archive(now.AddDays(1));
        Assert.Equal(now.ToUniversalTime(), exercise.ArchivedAt);
        Assert.Equal(TimeSpan.Zero, exercise.ArchivedAt!.Value.Offset);
        Assert.Equal("Squat", exercise.Name);
    }
}
