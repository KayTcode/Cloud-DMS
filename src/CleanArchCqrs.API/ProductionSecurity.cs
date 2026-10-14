using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace CleanArchCqrs.API;

internal static class ProductionSecurity
{
    public static IServiceCollection AddProductionSecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var appName = configuration["DataProtection:ApplicationName"];
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(appName))
            throw new InvalidOperationException("DataProtection application isolation name is required.");
        var protection = services.AddDataProtection().SetApplicationName(appName ?? "CloudDMS.Development");
        var keyPath = configuration["DataProtection:KeyRingPath"];
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(certificatePath)))
            throw new InvalidOperationException("Persistent encrypted DataProtection keys must be configured.");
        if (!string.IsNullOrWhiteSpace(keyPath) || !string.IsNullOrWhiteSpace(certificatePath))
        {
            if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(certificatePath))
                throw new InvalidOperationException("DataProtection key ring and certificate must be configured together.");
            var certificate = new X509Certificate2(certificatePath, configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new InvalidOperationException("DataProtection certificate must have a valid private key.");
            Directory.CreateDirectory(keyPath);
            var probe = Path.Combine(keyPath, $".write-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath)).ProtectKeysWithCertificate(certificate);
        }
        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(configuration["Encryption:SecretKey"]))
                throw new InvalidOperationException("A separate storage encryption secret is required.");
            var hosts = configuration["AllowedHosts"];
            if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Contains('*')))
                throw new InvalidOperationException("Explicit AllowedHosts are required.");
            if (!Uri.TryCreate(configuration["ReadAccess:PublicApiOrigin"], UriKind.Absolute, out var origin) || origin.Scheme != "https")
                throw new InvalidOperationException("Production PublicApiOrigin requires HTTPS.");
        }
        var cors = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (cors.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || (!environment.IsDevelopment() && uri.Scheme != "https")))
            throw new InvalidOperationException("Explicit HTTP(S) CORS origins are required.");
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            // Without a trusted peer never consume client-supplied forwarded headers.
            if (proxies.Length == 0) options.ForwardedHeaders = ForwardedHeaders.None;
            foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        return services;
    }
}
