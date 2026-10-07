using System;
using System.Threading;
using System.Web;
using System.Web.Hosting;
using Microsoft.AspNetCore.SystemWebAdapters;
using Microsoft.Extensions.Options;

namespace AspNetWebStack.Native;

// Initializes the existing adapter's virtual-path services for one application.
// The borrowed provider remains host-owned; no request, module or file host is created.
public sealed class NativePathHosting : IDisposable
{
    private static readonly object Gate = new();
    private HostingEnvironmentAccessor? _accessor;

    public NativePathHosting(IServiceProvider services, string applicationVirtualPath = "/")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(applicationVirtualPath);
        if (!applicationVirtualPath.StartsWith('/') ||
            (applicationVirtualPath.Length > 1 && applicationVirtualPath.EndsWith('/')) ||
            applicationVirtualPath.IndexOfAny(new[] { '\\', '?', '#', '%', '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("Use a canonical absolute application path.", nameof(applicationVirtualPath));
        if (applicationVirtualPath.Length > 1)
            foreach (string segment in applicationVirtualPath.Substring(1).Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new ArgumentException("Application paths cannot contain empty or relative segments.", nameof(applicationVirtualPath));
        var options = Options.Create(new SystemWebAdaptersOptions
        {
            AppDomainAppVirtualPath = applicationVirtualPath,
            ApplicationVirtualPath = applicationVirtualPath,
            IsHosted = true
        });
        lock (Gate)
        {
            _accessor = new HostingEnvironmentAccessor(new PathServices(services, options), options);
        }
    }

    public void Dispose()
    {
        lock (Gate) { Interlocked.Exchange(ref _accessor, null)?.Dispose(); }
    }

    private sealed class PathServices : IServiceProvider
    {
        private readonly IServiceProvider _services;
        private readonly VirtualPathUtilityImpl _paths;
        internal PathServices(IServiceProvider services, IOptions<SystemWebAdaptersOptions> options)
        {
            _services = services;
            // UrlPath's internal app-absolute conversion expects a trailing slash.
            // Keep public hosting metadata canonical; normalize only path services.
            string root = options.Value.AppDomainAppVirtualPath;
            var pathOptions = Options.Create(new SystemWebAdaptersOptions
            {
                AppDomainAppVirtualPath = root.EndsWith('/') ? root : root + "/",
                ApplicationVirtualPath = options.Value.ApplicationVirtualPath,
                IsHosted = true
            });
            _paths = new VirtualPathUtilityImpl(pathOptions);
        }
        public object? GetService(Type serviceType) => serviceType == typeof(VirtualPathUtilityImpl) ? _paths : _services.GetService(serviceType);
    }
}
