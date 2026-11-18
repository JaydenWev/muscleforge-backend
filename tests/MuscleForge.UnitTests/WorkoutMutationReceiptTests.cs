using MuscleForge.Core.Sessions;
using Xunit;

namespace MuscleForge.UnitTests;

public sealed class WorkoutMutationReceiptTests
{
    [Fact]
    public void ReceiptPreservesExactAcknowledgementAndValidatesIdentity()
    {
        var owner = Guid.NewGuid(); var mutation = Guid.NewGuid(); var session = Guid.NewGuid(); var hash = new string('b', 64); var at = DateTimeOffset.UtcNow;
        const string payload = " { \"version\": 2 } ";
        var receipt = new WorkoutMutationReceipt(owner, mutation, session, hash, 2, payload, at);
        Assert.Equal(payload, receipt.Acknowledgement);
        Assert.Throws<ArgumentException>(() => new WorkoutMutationReceipt(owner, mutation, session, hash, 2, "[]", at));
        Assert.Throws<ArgumentException>(() => new WorkoutMutationReceipt(owner, mutation, session, "bad", 2, payload, at));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkoutMutationReceipt(owner, mutation, session, hash, 0, payload, at));
    }
}
