using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Api.Contracts;
using MuscleForge.Core.Exercises;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class OwnerExerciseCatalogBootstrapTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConfiguredStartupMakesCommonExercisesAvailableThroughAuthenticatedApi()
    {
        await using var database = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString,
            OwnerBootstrapTests.Email, OwnerBootstrapTests.Password);
        Assert.True(api.Ready);
        api.AssertPrivate(OwnerBootstrapTests.Password, database.ConnectionString);
        using var client = new HttpClient { BaseAddress = api.Address };
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = OwnerBootstrapTests.Email, password = OwnerBootstrapTests.Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            token.RootElement.GetProperty("accessToken").GetString());

        var page = await client.GetFromJsonAsync<ExercisePageResponse>("/api/v1/exercises", Ct);
        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, exercise =>
        {
            Assert.False(exercise.IsArchived);
            Assert.Equal("ExternalWeightAndReps", exercise.TrackingType);
        });
        Assert.Contains(page.Items, exercise => exercise.LoadConvention == "TotalLoad");
        Assert.Contains(page.Items, exercise => exercise.LoadConvention == "PerDumbbell");
    }

    [Fact]
    public async Task StartupRetryAddsMissingSeedsWithoutChangingEditedArchivedOrCustomExercises()
    {
        await using var database = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await OwnerBootstrapTests.BootstrapOwnerAsync(database);
        await using var context = database.CreateContext();
        var seeds = await context.Exercises.OrderBy(x => x.SeedKey).ToListAsync(Ct);
        Assert.True(seeds.Count >= 2);
        var originalIds = seeds.Select(x => x.Id).ToHashSet();
        var edited = seeds[0];
        edited.Update("My preferred exercise", "My equipment", ExerciseLoadConvention.PerDumbbell);
        edited.Archive(DateTimeOffset.Parse("2026-01-01T12:00:00Z"));
        var missing = seeds[1];
        context.Exercises.Remove(missing); // Simulate a missing seed on an expendable test database.
        var custom = new Exercise(edited.OwnerId, missing.Name, "Custom equipment");
        context.Exercises.Add(custom);
        await context.SaveChangesAsync(Ct);

        await using var api = await OwnerApiProcess.StartAsync(database.ConnectionString,
            OwnerBootstrapTests.Email, OwnerBootstrapTests.Password);
        Assert.True(api.Ready);
        api.AssertPrivate(OwnerBootstrapTests.Password, database.ConnectionString);
        context.ChangeTracker.Clear();
        var after = await context.Exercises.ToListAsync(Ct);
        var restoredSeeds = after.Where(x => x.SeedKey is not null).ToList();
        Assert.Equal(seeds.Count, restoredSeeds.Count);
        Assert.Equal(seeds.Select(x => x.SeedKey).Order(), restoredSeeds.Select(x => x.SeedKey).Order());
        Assert.Equal(seeds.Count - 1, restoredSeeds.Count(x => originalIds.Contains(x.Id)));
        var preserved = Assert.Single(after, x => x.Id == edited.Id);
        Assert.Equal(edited.Name, preserved.Name);
        Assert.Equal(edited.Equipment, preserved.Equipment);
        Assert.Equal(edited.LoadConvention, preserved.LoadConvention);
        Assert.Equal(edited.ArchivedAt, preserved.ArchivedAt);
        Assert.Null(Assert.Single(after, x => x.Id == custom.Id).SeedKey);
    }
}
