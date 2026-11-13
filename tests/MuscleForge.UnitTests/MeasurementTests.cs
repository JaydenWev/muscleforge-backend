using System.Text.Json;
using MuscleForge.Core.Measurements;
using MuscleForge.Core.Time;
using Xunit;

namespace MuscleForge.UnitTests;

public sealed class MeasurementTests
{
    [Theory]
    [InlineData(0, WeightUnit.Kilograms, 0)]
    [InlineData(1, WeightUnit.Pounds, 0.454)]
    [InlineData(100, WeightUnit.Pounds, 45.359)]
    [InlineData(9999999.999, WeightUnit.Kilograms, 9999999.999)]
    public void ConvertsAtTheBoundary(decimal input, WeightUnit unit, decimal expected) =>
        Assert.Equal(expected, ExternalWeight.ToKilograms(input, unit));

    [Fact]
    public void SharedExamplesMatchConversionAndLocalDays()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "units-timezones.json")));
        foreach (var item in fixture.RootElement.GetProperty("weightConversions").EnumerateArray())
        {
            var unit = item.GetProperty("unit").GetString() == "kg" ? WeightUnit.Kilograms : WeightUnit.Pounds;
            Assert.Equal(item.GetProperty("kilograms").GetDecimal(), ExternalWeight.ToKilograms(item.GetProperty("value").GetDecimal(), unit));
        }
        foreach (var item in fixture.RootElement.GetProperty("invalidWeights").EnumerateArray())
        {
            var unit = item.GetProperty("unit").GetString() == "kg" ? WeightUnit.Kilograms : WeightUnit.Pounds;
            Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(item.GetProperty("value").GetDecimal(), unit));
        }
        foreach (var item in fixture.RootElement.GetProperty("localDays").EnumerateArray())
            Assert.Equal(DateOnly.Parse(item.GetProperty("localDay").GetString()!), SessionTimeZone.LocalDay(item.GetProperty("instant").GetDateTimeOffset(), item.GetProperty("timeZoneId").GetString()!));
    }

    [Fact]
    public void RejectsInvalidAndOutOfRangeInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(-0.001m, WeightUnit.Kilograms));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(1.0000m, WeightUnit.Kilograms));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(10_000_000m, WeightUnit.Kilograms));
        Assert.Equal(9_999_999.999m, ExternalWeight.ToKilograms(22_046_226.216m, WeightUnit.Pounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(22_046_226.217m, WeightUnit.Pounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(decimal.MaxValue, WeightUnit.Pounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalWeight.ToKilograms(1m, (WeightUnit)99));
    }

    [Theory]
    [InlineData("2024-03-31T00:30:00+00:00", "Europe/Amsterdam", 2024, 3, 31)]
    [InlineData("2024-03-31T22:30:00+00:00", "Europe/Amsterdam", 2024, 4, 1)]
    [InlineData("2024-10-27T00:30:00+00:00", "America/New_York", 2024, 10, 26)]
    public void DerivesLocalDayFromInstant(string instant, string zone, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), SessionTimeZone.LocalDay(DateTimeOffset.Parse(instant), zone));

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("Pacific Standard Time")]
    public void RejectsUnknownAndWindowsZones(string zone) =>
        Assert.Throws<ArgumentException>(() => SessionTimeZone.Resolve(zone));
}
