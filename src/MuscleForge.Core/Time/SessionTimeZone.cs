namespace MuscleForge.Core.Time;

/// <summary>Resolves a captured IANA time zone for deriving a session's local day.</summary>
public static class SessionTimeZone
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) && zone.HasIanaId;

    public static TimeZoneInfo Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) || !zone.HasIanaId)
        {
            throw new ArgumentException("A known IANA time zone is required.", nameof(id));
        }

        return zone;
    }

    public static DateOnly LocalDay(DateTimeOffset instant, string id) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Resolve(id)).DateTime);
}
