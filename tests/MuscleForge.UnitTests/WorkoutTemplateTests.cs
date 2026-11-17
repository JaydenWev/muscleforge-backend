using MuscleForge.Core.Exercises;
using MuscleForge.Core.Templates;
using MuscleForge.Core.Workouts;
using Xunit;

namespace MuscleForge.UnitTests;

public sealed class WorkoutTemplateTests
{
    [Fact]
    public void TemplateEnforcesOwnershipPositionsAndArchive()
    {
        var owner = Guid.NewGuid(); var template = new WorkoutTemplate(owner, " Strength ");
        Assert.Equal("Strength", template.Name); Assert.Equal(1, template.Version);
        Assert.Throws<ArgumentException>(() => template.AddExercise(new Exercise(Guid.NewGuid(), "Squat", "Barbell"), 0));
        var item = template.AddExercise(new Exercise(owner, "Squat", "Barbell"), 0);
        template.AddSet(item.Id, 0, SetKind.Working, 10.125m, 5, 8);
        Assert.Throws<InvalidOperationException>(() => template.AddSet(item.Id, 0, SetKind.Warmup, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.AddSet(item.Id, 1, SetKind.Working, 1.0001m, 5, 8));
        template.AdvanceVersion(); template.Archive(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => template.AdvanceVersion());
        Assert.Throws<InvalidOperationException>(() => template.Rename("Changed"));
        Assert.Throws<InvalidOperationException>(() => template.AddSet(item.Id, 1, SetKind.Working, null, null, null));
    }
}
