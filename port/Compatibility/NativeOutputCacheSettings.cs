namespace System.Web.UI;

// Source-compatible attribute configuration only; no legacy Page lifecycle.
public enum OutputCacheLocation { Any = 0, Client = 1, Downstream = 2, Server = 3, None = 4, ServerAndClient = 5 }
internal sealed class OutputCacheParameters
{
    internal string CacheProfile { get; set; }
    internal int Duration { get; set; }
    internal OutputCacheLocation Location { get; set; } = OutputCacheLocation.Any;
    internal bool NoStore { get; set; }
    internal string SqlDependency { get; set; }
    internal string VaryByContentEncoding { get; set; }
    internal string VaryByCustom { get; set; }
    internal string VaryByHeader { get; set; }
    internal string VaryByParam { get; set; }
}
