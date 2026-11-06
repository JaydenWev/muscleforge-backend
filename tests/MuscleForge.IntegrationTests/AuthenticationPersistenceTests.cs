using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MuscleForge.IntegrationTests;

public sealed partial class AuthenticationTests
{
    [Fact]
    public async Task ProductionRefusesMissingPersistentKeyConfiguration()
    {
        await using var db = await SetupAsync();
        await using var api = db.CreateApi().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysPath"] = null,
                ["DataProtection:CertificatePath"] = null,
                ["DataProtection:CertificatePassword"] = null
            }));
        });
        var failure = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.DoesNotContain(db.ConnectionString, failure.ToString(), StringComparison.Ordinal);
        Assert.Contains("DataProtection", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EncryptedPersistentKeysSurviveHostRestartAndDifferentKeysRejectTokens()
    {
        await using var db = await SetupAsync();
        using var keys = new KeyDirectory();
        using var otherKeys = new KeyDirectory();
        Token token;
        await using (var first = CreateApi(db, keys: keys))
        using (var client = first.CreateClient())
            token = await Login(client);
        var xml = string.Join('\n', Directory.GetFiles(keys.Path, "*.xml").Select(File.ReadAllText));
        Assert.Contains("encryptedSecret", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<masterKey", xml, StringComparison.OrdinalIgnoreCase);
        await using (var second = CreateApi(db, keys: keys))
        using (var client = second.CreateClient())
        {
            using var me = await GetMe(client, token.AccessToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token.RefreshToken }, Ct);
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
        await using (var different = CreateApi(db, keys: otherKeys))
        using (var client = different.CreateClient())
        {
            using var me = await GetMe(client, token.AccessToken);
            await Problem(me, 401, "authentication_required");
            using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token.RefreshToken }, Ct);
            await Problem(refresh, 401, "authentication_required");
        }
    }

}
