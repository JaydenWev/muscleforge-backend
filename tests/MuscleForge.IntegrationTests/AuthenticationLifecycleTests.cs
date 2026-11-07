using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MuscleForge.Infrastructure.Identity;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed partial class AuthenticationTests
{
    [Fact]
    public async Task ExpiryAndSecurityStampResetHaveDistinctEffects()
    {
        await using var db = await SetupAsync();
        var clock = new AdjustableTimeProvider();
        await using var api = CreateApi(db, clock);
        using var client = api.CreateClient();
        var token = await Login(client);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        }
        using var rejected = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token.RefreshToken }, Ct);
        await Problem(rejected, 401, "authentication_required");
        using var stillValid = await GetMe(client, token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        var fresh = await Login(client);
        clock.Advance(TimeSpan.FromMinutes(16));
        using var expiredAccess = await GetMe(client, fresh.AccessToken);
        await Problem(expiredAccess, 401, "authentication_required");
        clock.Advance(TimeSpan.FromDays(14));
        using var expiredRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = fresh.RefreshToken }, Ct);
        await Problem(expiredRefresh, 401, "authentication_required");
    }

    [Fact]
    public async Task FailedPasswordsLockAccountAndRecoveryFollowsLockoutWindow()
    {
        await using var db = await SetupAsync();
        await using var api = CreateApi(db);
        using var client = api.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var failure = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = "Wrong-Synthetic!2468" }, Ct);
            await Problem(failure, 401, "authentication_required");
        }
        using var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password }, Ct);
        await Problem(locked, 401, "authentication_required");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            Assert.InRange(user.LockoutEnd!.Value, DateTimeOffset.UtcNow.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(5));
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddSeconds(-1))).Succeeded);
        }
        _ = await Login(client);
    }

}
