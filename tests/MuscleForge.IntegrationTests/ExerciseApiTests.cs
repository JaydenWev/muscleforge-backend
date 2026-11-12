using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MuscleForge.Infrastructure.Identity;
using Xunit;

namespace MuscleForge.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class ExerciseApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = "Synthetic-Password!2468";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnerCrudArchiveAndIsolationUsePersistedIdentity()
    {
        await using var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await using var api = db.CreateApi();
        var (owner, other) = await CreateUsers(api.Services);
        using var first = api.CreateClient();
        using var second = api.CreateClient();
        await Authenticate(first, "first@example.test");
        await Authenticate(second, "second@example.test");
        using var created = await first.PostAsJsonAsync("/api/v1/exercises", new { name = "  Press  ", equipment = " Dumbbell ", ownerId = other, seedKey = "injected" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var root = body.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/exercises/{id}", created.Headers.Location?.OriginalString);
        Assert.Equal("Press", root.GetProperty("name").GetString());
        Assert.Equal("Dumbbell", root.GetProperty("equipment").GetString());
        Assert.Equal("ExternalWeightAndReps", root.GetProperty("trackingType").GetString());
        Assert.Equal("TotalLoad", root.GetProperty("loadConvention").GetString());
        Assert.False(root.TryGetProperty("ownerId", out _));
        Assert.False(root.TryGetProperty("seedKey", out _));
        await using (var context = db.CreateContext())
            Assert.Equal(owner, (await context.Exercises.SingleAsync(x => x.Id == id, Ct)).OwnerId);
        foreach (var target in new[] { id, Guid.NewGuid() })
        {
            using var get = await second.GetAsync($"/api/v1/exercises/{target}", Ct);
            await Problem(get, 404, "resource_not_found");
            using var put = await second.PutAsJsonAsync($"/api/v1/exercises/{target}", new { name = "Stolen", equipment = "Bar", isArchived = true }, Ct);
            await Problem(put, 404, "resource_not_found");
        }
        using (var otherList = await second.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?includeArchived=true", Ct))
            Assert.Equal(0, otherList!.RootElement.GetProperty("totalCount").GetInt32());
        using (var original = await first.GetFromJsonAsync<JsonDocument>($"/api/v1/exercises/{id}", Ct))
        {
            Assert.Equal("Press", original!.RootElement.GetProperty("name").GetString());
            Assert.False(original.RootElement.GetProperty("isArchived").GetBoolean());
        }
        using var archived = await first.PutAsJsonAsync($"/api/v1/exercises/{id}", new { name = "Press", equipment = "Dumbbell", loadConvention = "PerDumbbell", isArchived = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var archivedBody = JsonDocument.Parse(await archived.Content.ReadAsStringAsync(Ct));
        var timestamp = archivedBody.RootElement.GetProperty("archivedAt").GetDateTimeOffset();
        Assert.Equal("PerDumbbell", archivedBody.RootElement.GetProperty("loadConvention").GetString());
        using var conflict = await first.PutAsJsonAsync($"/api/v1/exercises/{id}", new { name = "Changed", equipment = "Bar", isArchived = false }, Ct);
        await Problem(conflict, 409, "state_conflict");
        using var edited = await first.PutAsJsonAsync($"/api/v1/exercises/{id}", new { name = "Edited", equipment = "Dumbbell", isArchived = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var editedBody = JsonDocument.Parse(await edited.Content.ReadAsStringAsync(Ct));
        Assert.Equal(timestamp, editedBody.RootElement.GetProperty("archivedAt").GetDateTimeOffset());
        using var list = await first.GetFromJsonAsync<JsonDocument>("/api/v1/exercises", Ct);
        Assert.Equal(0, list!.RootElement.GetProperty("totalCount").GetInt32());
        using var all = await first.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?includeArchived=true", Ct);
        Assert.Equal(1, all!.RootElement.GetProperty("totalCount").GetInt32());
        using var byId = await first.GetFromJsonAsync<JsonDocument>($"/api/v1/exercises/{id}", Ct);
        Assert.Equal("Edited", byId!.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SearchIsLiteralAndPagedInStableOrder()
    {
        await using var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await using var api = db.CreateApi();
        await CreateUsers(api.Services);
        using var client = api.CreateClient();
        await Authenticate(client, "first@example.test");
        foreach (var name in new[] { "Zebra", "Alpha 100%", "Alpha 100%", "Alpha 100x", "Beta_" })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/exercises", new { name, equipment = "Bar" }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        using (var response = await client.PostAsJsonAsync("/api/v1/exercises", new { name = "Cable row", equipment = "Cable\\handle" }, Ct))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var backslash = await client.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?search=%5C", Ct);
        Assert.Equal(1, backslash!.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Cable row", backslash.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
        using var equipmentSearch = await client.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?search=cAbLe%5ChAnDlE", Ct);
        Assert.Equal(1, equipmentSearch!.RootElement.GetProperty("totalCount").GetInt32());
        using var percent = await client.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?search=%25&limit=1&offset=1", Ct);
        Assert.Equal(2, percent!.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(percent.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1, percent.RootElement.GetProperty("offset").GetInt32());
        using var underscore = await client.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?search=_", Ct);
        Assert.Equal(1, underscore!.RootElement.GetProperty("totalCount").GetInt32());
        using var page = await client.GetFromJsonAsync<JsonDocument>("/api/v1/exercises?search=ALPHA", Ct);
        var items = page!.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.True(items[0].GetProperty("id").GetGuid().CompareTo(items[1].GetProperty("id").GetGuid()) < 0);
    }

    [Fact]
    public async Task AuthenticationAndInputValidationCoverAllRoutes()
    {
        await using var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await using var api = db.CreateApi();
        await CreateUsers(api.Services);
        using var client = api.CreateClient();
        var id = Guid.NewGuid();
        using (var response = await client.GetAsync("/api/v1/exercises", Ct)) await Problem(response, 401, "authentication_required");
        using (var response = await client.GetAsync($"/api/v1/exercises/{id}", Ct)) await Problem(response, 401, "authentication_required");
        using (var response = await client.PostAsJsonAsync("/api/v1/exercises", new { name = "X", equipment = "Bar" }, Ct)) await Problem(response, 401, "authentication_required");
        using (var response = await client.PutAsJsonAsync($"/api/v1/exercises/{id}", new { name = "X", equipment = "Bar", isArchived = false }, Ct)) await Problem(response, 401, "authentication_required");
        await Authenticate(client, "first@example.test");
        foreach (var query in new[] { "offset=-1", "limit=0", "limit=101", "search=" + new string('a', 101) })
        {
            using var response = await client.GetAsync("/api/v1/exercises?" + query, Ct);
            await Problem(response, 400, "validation_failed");
        }
        foreach (var query in new[] { "offset=abc", "limit=abc", "includeArchived=perhaps" })
        {
            using var response = await client.GetAsync("/api/v1/exercises?" + query, Ct);
            await Problem(response, 400, "invalid_request");
        }
        using (var boundary = await client.PostAsJsonAsync("/api/v1/exercises", new { name = "  " + new string('a', 100) + " ", equipment = " " + new string('b', 100) + "  " }, Ct))
            Assert.Equal(HttpStatusCode.Created, boundary.StatusCode);
        foreach (var payload in new object[] {
            new { equipment = "Bar" }, new { name = (string?)null, equipment = "Bar" },
            new { name = "\u2003", equipment = "Bar" }, new { name = "X" },
            new { name = "X", equipment = (string?)null }, new { name = "X", equipment = "\u2003" },
            new { name = new string('a', 101), equipment = "Bar" }, new { name = "X", equipment = new string('b', 101) },
            new { name = "X", equipment = "Bar", trackingType = "Other" }, new { name = "X", equipment = "Bar", trackingType = (string?)null },
            new { name = "X", equipment = "Bar", loadConvention = "Other" }, new { name = "X", equipment = "Bar", loadConvention = (string?)null } })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/exercises", payload, Ct);
            await Problem(response, 400, "validation_failed");
        }
        using (var response = await client.PutAsJsonAsync($"/api/v1/exercises/{id}", new { name = "X", equipment = "Bar" }, Ct)) await Problem(response, 400, "validation_failed");
        using (var response = await client.PostAsync("/api/v1/exercises", new StringContent("{", Encoding.UTF8, "application/json"), Ct)) await Problem(response, 400, "invalid_request");
    }

    [Fact]
    public async Task ConcurrentUpdatesSerializeArchiveTransitionAndPreserveTimestamp()
    {
        await using var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        var clock = new AdvancingClock();
        await using var api = db.CreateApi().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        await CreateUsers(api.Services);
        using var client = api.CreateClient();
        await Authenticate(client, "first@example.test");
        using var created = await client.PostAsJsonAsync("/api/v1/exercises", new { name = "Press", equipment = "Bar" }, Ct);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = body.RootElement.GetProperty("id").GetGuid();
        var url = $"/api/v1/exercises/{id}";
        var first = client.PutAsJsonAsync(url, new { name = "First", equipment = "Bar", isArchived = true }, Ct);
        var second = client.PutAsJsonAsync(url, new { name = "Second", equipment = "Bar", isArchived = true }, Ct);
        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync(Ct));
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync(Ct));
        var archivedAt = firstJson.RootElement.GetProperty("archivedAt").GetDateTimeOffset();
        Assert.Equal(archivedAt, secondJson.RootElement.GetProperty("archivedAt").GetDateTimeOffset());
        var archive = client.PutAsJsonAsync(url, new { name = "Archived", equipment = "Bar", isArchived = true }, Ct);
        var unarchive = client.PutAsJsonAsync(url, new { name = "Unarchived", equipment = "Bar", isArchived = false }, Ct);
        using var archiveResponse = await archive;
        using var unarchiveResponse = await unarchive;
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        await Problem(unarchiveResponse, 409, "state_conflict");
        using var stored = await client.GetFromJsonAsync<JsonDocument>(url, Ct);
        Assert.Equal(archivedAt, stored!.RootElement.GetProperty("archivedAt").GetDateTimeOffset());
        Assert.True(stored.RootElement.GetProperty("isArchived").GetBoolean());

        using var another = await client.PostAsJsonAsync("/api/v1/exercises", new { name = "Squat", equipment = "Bar" }, Ct);
        using var anotherBody = JsonDocument.Parse(await another.Content.ReadAsStringAsync(Ct));
        var anotherUrl = $"/api/v1/exercises/{anotherBody.RootElement.GetProperty("id").GetGuid()}";
        var transition = client.PutAsJsonAsync(anotherUrl, new { name = "Archive", equipment = "Bar", isArchived = true }, Ct);
        var competingEdit = client.PutAsJsonAsync(anotherUrl, new { name = "Edit", equipment = "Bar", isArchived = false }, Ct);
        using var transitionResponse = await transition;
        using var editResponse = await competingEdit;
        Assert.Equal(HttpStatusCode.OK, transitionResponse.StatusCode);
        Assert.Contains(editResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        if (editResponse.StatusCode == HttpStatusCode.OK)
        {
            using var editJson = JsonDocument.Parse(await editResponse.Content.ReadAsStringAsync(Ct));
            Assert.False(editJson.RootElement.GetProperty("isArchived").GetBoolean());
        }
        else await Problem(editResponse, 409, "state_conflict");
        using var transitionJson = JsonDocument.Parse(await transitionResponse.Content.ReadAsStringAsync(Ct));
        using var final = await client.GetFromJsonAsync<JsonDocument>(anotherUrl, Ct);
        Assert.True(final!.RootElement.GetProperty("isArchived").GetBoolean());
        Assert.Equal(transitionJson.RootElement.GetProperty("archivedAt").GetDateTimeOffset(), final.RootElement.GetProperty("archivedAt").GetDateTimeOffset());
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    [Fact]
    public async Task DeletedAccountBearerIsRejectedEvenIfTokenIsStillValid()
    {
        await using var db = await OwnerBootstrapTests.CreateMigratedDatabaseAsync(postgres);
        await using var api = db.CreateApi();
        await CreateUsers(api.Services);
        using var client = api.CreateClient();
        await Authenticate(client, "first@example.test");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("first@example.test"))!;
            Assert.True((await users.DeleteAsync(user)).Succeeded);
        }
        using var response = await client.GetAsync("/api/v1/exercises", Ct);
        await Problem(response, 401, "authentication_required");
    }

    private static async Task<(Guid, Guid)> CreateUsers(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var first = new ApplicationUser { UserName = "first@example.test", Email = "first@example.test" };
        var second = new ApplicationUser { UserName = "second@example.test", Email = "second@example.test" };
        Assert.True((await users.CreateAsync(first, Password)).Succeeded);
        Assert.True((await users.CreateAsync(second, Password)).Succeeded);
        return (first.Id, second.Id);
    }
    private static async Task Authenticate(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
    }
    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
}
