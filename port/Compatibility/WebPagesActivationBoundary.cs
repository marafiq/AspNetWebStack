using System;

namespace System.Web.WebPages
{
    // A missing explicit registration must fail, never masquerade as a missing file.
    // The original manager still owns registration order and first-match activation.
    internal sealed class NativeUnsupportedVirtualPathFactory : IVirtualPathFactory
    {
        public bool Exists(string virtualPath) { throw Unavailable(); }
        public object CreateInstance(string virtualPath) { throw Unavailable(); }
        private static Exception Unavailable()
        {
            return new PlatformNotSupportedException("Default WebPages BuildManager activation is unavailable. Register an explicit IVirtualPathFactory that owns this path.");
        }
    }
}
