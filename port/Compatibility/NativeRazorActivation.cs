using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.WebPages;

namespace System.Web.Mvc
{
    internal sealed class NativeUnavailableBuildManager : IBuildManager
    {
        public bool FileExists(string path) { throw Unavailable(); }
        public Type GetCompiledType(string path) { throw Unavailable(); }
        public ICollection GetReferencedAssemblies() { throw Unavailable(); }
        public Stream ReadCachedFile(string path) { throw Unavailable(); }
        public Stream CreateCachedFile(string path) { throw Unavailable(); }
        private static Exception Unavailable() { return new PlatformNotSupportedException("Default ASP.NET BuildManager hosting is unavailable. Supply a native compiled-view registration."); }
    }

    internal static class NativeRazorHosting
    {
        // Preserve the explicit failure for callers without native path hosting.
        internal const string ViewStartFileName = "_ViewStart";
        internal static WebPageRenderingBase GetStartPage(WebPageRenderingBase page, string name, IEnumerable<string> extensions)
        {
            if (!System.Web.Hosting.HostingEnvironment.IsHosted)
                throw new PlatformNotSupportedException("Automatic Razor start-page discovery requires initialized native path hosting.");
            return StartPage.GetStartPage(page, name, extensions);
        }
    }
}
