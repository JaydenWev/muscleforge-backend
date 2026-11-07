using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace MuscleForge.Api.Authentication;

public static class AuthDataProtection
{
    public static IServiceCollection AddAuthDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddSingleton<CertificateHolder>();
        services.AddSingleton<IDataProtectionProvider>(provider =>
        {
            var settings = provider.GetRequiredService<IConfiguration>();
            var path = settings["DataProtection:KeysPath"];
            if (string.IsNullOrWhiteSpace(path))
                return new EphemeralDataProtectionProvider();

            return DataProtectionProvider.Create(new DirectoryInfo(path), protection =>
            {
                protection.SetApplicationName("MuscleForge");
                if (!string.IsNullOrWhiteSpace(settings["DataProtection:CertificatePath"]))
                    protection.ProtectKeysWithCertificate(provider.GetRequiredService<CertificateHolder>().Certificate);
            });
        });
        return services;
    }

    public static void ValidateAuthDataProtection(this WebApplication app)
    {
        var configuration = app.Configuration;
        var keysPath = configuration["DataProtection:KeysPath"];
        var certPath = configuration["DataProtection:CertificatePath"];
        if (app.Environment.IsProduction() && (string.IsNullOrWhiteSpace(keysPath) || string.IsNullOrWhiteSpace(certPath)))
            throw new InvalidOperationException("Configure DataProtection:KeysPath and DataProtection:CertificatePath before starting the API.");
        if (!string.IsNullOrWhiteSpace(certPath))
        {
            try
            {
                using var certificate = LoadCertificate(configuration);
                if (!certificate.HasPrivateKey)
                    throw new InvalidOperationException();
            }
            catch (Exception)
            {
                throw new InvalidOperationException("The configured data protection certificate could not be loaded with a private key.");
            }
        }
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration) =>
        X509CertificateLoader.LoadPkcs12FromFile(configuration["DataProtection:CertificatePath"]!,
            configuration["DataProtection:CertificatePassword"]);

    private sealed class CertificateHolder(IConfiguration configuration) : IDisposable
    {
        public X509Certificate2 Certificate { get; } = LoadCertificate(configuration);
        public void Dispose() => Certificate.Dispose();
    }
}
