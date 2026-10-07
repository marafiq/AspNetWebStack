using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
namespace Stockroom.Hosting;

// Trusted composition seam for embedding hosts. Configuration cannot select an implementation.
// The executable always uses DeploymentInfrastructure; verification supplies memory-only services.
public interface IHostInfrastructure
{
    IDisposable Configure(WebApplicationBuilder builder, HostSettings settings);
}
internal sealed class DeploymentInfrastructure : IHostInfrastructure
{
    public IDisposable Configure(WebApplicationBuilder builder, HostSettings settings)
    {
        var s = settings.Deployment;
        var owned = new Certificates();
        try {
            var encryption = owned.Load(s.KeyEncryption);
            if (encryption.GetRSAPublicKey() is not { } rsa) throw new ArgumentException("Key encryption requires an RSA certificate.");
            rsa.Dispose();
            var previous = s.PreviousKeyEncryption.Select(file => owned.Load(file, requireCurrent: false)).ToArray();
            builder.Services.AddDataProtection().SetApplicationName(s.ApplicationName)
                .PersistKeysToFileSystem(new DirectoryInfo(s.KeyDirectory))
                .ProtectKeysWithCertificate(encryption).UnprotectKeysWithAnyCertificate(new[] { encryption }.Concat(previous).ToArray());
            var tls = s.TrustedProxy ? null : owned.Load(s.Https);
            builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 65536; o.Listen(s.ListenAddress, s.Port, listen => { if (tls != null) listen.UseHttps(tls); }); });
            return owned;
        } catch { owned.Dispose(); throw; }
    }
    private sealed class Certificates : IDisposable
    {
        private readonly List<X509Certificate2> _certificates = new();
        internal X509Certificate2 Load(CertificateFile file, bool requireCurrent = true) {
            // Only the actual deployment path executes this. No OS certificate-store lookup or key import persistence.
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(file.Path, file.Password, X509KeyStorageFlags.EphemeralKeySet);
            _certificates.Add(certificate);
            if (!certificate.HasPrivateKey || (requireCurrent && (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)))
                throw new ArgumentException("A configured certificate has no private key or is outside its validity period.");
            return certificate;
        }
        public void Dispose() { foreach (var certificate in _certificates) certificate.Dispose(); }
    }
}
