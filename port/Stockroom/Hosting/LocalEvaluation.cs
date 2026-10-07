using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
namespace Stockroom.Hosting;

// Process-only local evaluation. HostSettings selects this explicitly before Build.
internal sealed class LocalEvaluation : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly X509Certificate2 _certificate;
    private LocalEvaluation(WebApplicationBuilder builder)
    {
        var request = new CertificateRequest("CN=localhost", _key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(8));
        int port = builder.Configuration.GetValue<int?>("Port") ?? 7443;
        builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 65536; o.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(_certificate)); });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider()
            .AddKeyManagementOptions(o => o.XmlRepository = new MemoryKeys());
    }
    internal static LocalEvaluation Configure(WebApplicationBuilder builder)
    {
        return new LocalEvaluation(builder);
    }
    internal static void Verify(IServiceProvider services)
    {
        if (services.GetRequiredService<IDataProtectionProvider>() is not EphemeralDataProtectionProvider ||
            services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository is not MemoryKeys)
            throw new InvalidOperationException("Local evaluation must use process-only payload protection and key repository.");
    }
    public void Dispose() { _certificate.Dispose(); _key.Dispose(); }
    private sealed class MemoryKeys : IXmlRepository
    {
        private readonly List<XElement> _keys = new();
        public IReadOnlyCollection<XElement> GetAllElements() { lock (_keys) return _keys.Select(x => new XElement(x)).ToArray(); }
        public void StoreElement(XElement value, string name) { lock (_keys) _keys.Add(new XElement(value)); }
    }
}
