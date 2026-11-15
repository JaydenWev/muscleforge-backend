namespace MuscleForge.Core.Measurements;

public enum WeightUnit
{
    Kilograms,
    Pounds
}

/// <summary>Conversion and validation for external load at a future API boundary.</summary>
public static class ExternalWeight
{
    public const decimal KilogramsPerPound = 0.45359237m;
    public const decimal MaximumKilograms = 9_999_999.999m;

    public static bool AreCompletedRepetitionsValid(int repetitions) => repetitions > 0;

    public static decimal ToKilograms(decimal value, WeightUnit unit)
    {
        if (value < 0 || ((decimal.GetBits(value)[3] >> 16) & 0xff) > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Weight must be nonnegative with at most three decimal places.");
        }

        // Check before multiplication to avoid overflow on hostile inputs.
        var maximumInput = unit switch
        {
            WeightUnit.Kilograms => MaximumKilograms,
            WeightUnit.Pounds => MaximumKilograms / KilogramsPerPound,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        if (value > maximumInput)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Weight exceeds the supported range.");
        }

        var kilograms = unit == WeightUnit.Kilograms ? value : value * KilogramsPerPound;
        var rounded = decimal.Round(kilograms, 3, MidpointRounding.AwayFromZero);
        if (rounded > MaximumKilograms)
            throw new ArgumentOutOfRangeException(nameof(value), "Weight exceeds the supported range.");
        return rounded;
    }
}
