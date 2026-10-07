using System.Net;
namespace Stockroom.Hosting;

public sealed class HostSettings
{
    public bool IsDeployment { get; private init; }
    public string PathBase { get; private init; }
    public DeploymentSettings Deployment { get; private init; }
    public static HostSettings Read(IConfiguration config)
    {
        string mode = config["HostMode"];
        string legacy = config["LocalEvaluation"];
        if (legacy != null) {
            if (!Boolean.TryParse(legacy, out bool enabled) || !enabled || (mode != null && mode != "LocalEvaluation"))
                throw new ArgumentException("LocalEvaluation=true is an alias only for HostMode=LocalEvaluation.");
            mode = "LocalEvaluation";
        }
        if (mode != "LocalEvaluation" && mode != "Deployment") throw new ArgumentException("Choose HostMode=LocalEvaluation or HostMode=Deployment explicitly. See README.md.");
        string mount = config["PathBase"] ?? "/";
        if (mount != "/" && (!mount.StartsWith('/') || mount.EndsWith('/') || mount.Contains("//") || mount.Contains("..") || mount.Any(c => !(Char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_'))))
            throw new ArgumentException("PathBase must be / or a plain absolute path without a trailing slash.");
        return new HostSettings { IsDeployment = mode == "Deployment", PathBase = mount, Deployment = mode == "Deployment" ? DeploymentSettings.Read(config.GetSection("Deployment")) : null };
    }
}
public sealed class DeploymentSettings
{
    public Uri PublicOrigin { get; private init; }
    public string Authority { get; private init; }
    public string Issuer { get; private init; }
    public string ClientId { get; private init; }
    public string ClientSecret { get; private init; }
    public string NameClaim { get; private init; }
    public string RoleClaim { get; private init; }
    public string ApplicationName { get; private init; }
    public string KeyDirectory { get; private init; }
    public CertificateFile KeyEncryption { get; private init; }
    public CertificateFile[] PreviousKeyEncryption { get; private init; }
    public CertificateFile Https { get; private init; }
    public string DatabasePath { get; private init; }
    public bool TrustedProxy { get; private init; }
    public IPAddress ListenAddress { get; private init; }
    public int Port { get; private init; }
    public IPAddress[] KnownProxies { get; private init; }
    internal static DeploymentSettings Read(IConfiguration c)
    {
        string Required(string key, int max = 512) => Require(c[key], "Deployment:" + key, max);
        Uri origin = HttpsUri(Required("PublicOrigin"), "PublicOrigin");
        if (origin.AbsolutePath != "/") throw new ArgumentException("PublicOrigin must contain only the HTTPS origin; use PathBase for a mount.");
        string authority = Required("Identity:Authority"), issuer = Required("Identity:Issuer");
        HttpsUri(authority, "Identity:Authority"); HttpsUri(issuer, "Identity:Issuer");
        string transport = Required("Transport:Mode");
        if (transport != "DirectHttps" && transport != "TrustedProxy") throw new ArgumentException("Transport:Mode must be DirectHttps or TrustedProxy.");
        if (!IPAddress.TryParse(Required("Transport:ListenAddress"), out var address)) throw new ArgumentException("ListenAddress must be an explicit IP address.");
        if (!Int32.TryParse(Required("Transport:Port"), out int port) || port < 1 || port > 65535) throw new ArgumentException("Deployment port must be 1–65535.");
        var proxies = c.GetSection("Transport:KnownProxies").GetChildren().Select(x => IPAddress.TryParse(x.Value, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) ? ip : throw new ArgumentException("Each KnownProxies entry must be a concrete IP address.")).ToArray();
        if (proxies.Length > 8 || (transport == "TrustedProxy" && proxies.Length == 0) || (transport == "DirectHttps" && proxies.Length != 0)) throw new ArgumentException("TrustedProxy requires 1–8 known proxy IPs; DirectHttps accepts none.");
        var previous = c.GetSection("DataProtection:PreviousCertificates").GetChildren().Select(CertificateFile.Read).ToArray();
        if (previous.Length > 4) throw new ArgumentException("At most four previous key-encryption certificates are supported.");
        var settings = new DeploymentSettings {
            PublicOrigin = origin, Authority = authority, Issuer = issuer,
            ClientId = Required("Identity:ClientId", 256), ClientSecret = Required("Identity:ClientSecret", 4096),
            NameClaim = Required("Identity:NameClaim", 128), RoleClaim = Required("Identity:RoleClaim", 128),
            ApplicationName = Required("DataProtection:ApplicationName", 128),
            KeyDirectory = AbsolutePath(Required("DataProtection:KeyDirectory"), "KeyDirectory"),
            KeyEncryption = CertificateFile.Read(c.GetSection("DataProtection:Certificate")), PreviousKeyEncryption = previous,
            Https = transport == "DirectHttps" ? CertificateFile.Read(c.GetSection("Transport:Certificate")) : null,
            DatabasePath = AbsolutePath(Required("State:DatabasePath"), "DatabasePath"),
            TrustedProxy = transport == "TrustedProxy", ListenAddress = address, Port = port, KnownProxies = proxies
        };
        if (settings.DatabasePath == settings.KeyDirectory || settings.DatabasePath.StartsWith(settings.KeyDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || settings.DatabasePath == settings.KeyEncryption.Path || previous.Any(x => x.Path == settings.DatabasePath) || settings.Https?.Path == settings.DatabasePath)
            throw new ArgumentException("Business data must be separate from key and certificate locations.");
        return settings;
    }
    internal static string Require(string value, string key, int max = 512) => !String.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(Char.IsControl) && !value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ? value : throw new ArgumentException("Missing or invalid configuration: " + key);
    internal static string AbsolutePath(string value, string key)
    {
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException(key + " must be an absolute non-root path.");
        string canonical = Path.GetFullPath(value);
        if (Path.TrimEndingDirectorySeparator(canonical) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(canonical)))
            throw new ArgumentException(key + " must be an absolute non-root path.");
        return canonical;
    }
    private static Uri HttpsUri(string value, string key) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "" ? uri : throw new ArgumentException(key + " must be an absolute HTTPS URL without credentials/query/fragment.");
}
public sealed class CertificateFile
{
    public string Path { get; private init; }
    public string Password { get; private init; }
    internal static CertificateFile Read(IConfigurationSection c) => new() {
        Path = DeploymentSettings.AbsolutePath(DeploymentSettings.Require(c["Path"], c.Path + ":Path"), c.Path),
        Password = DeploymentSettings.Require(c["Password"], c.Path + ":Password", 4096)
    };
}
