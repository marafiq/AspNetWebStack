using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetWebStack.Native
{
    /// <summary>Explicit MVC dependency resolution over the native host's services.</summary>
    /// <remarks>
    /// Install once during application startup. Native DI owns all service lifetimes.
    /// MVC-cached providers and metadata services must have application lifetime;
    /// singleton providers can resolve request services when MVC invokes them.
    /// </remarks>
    public sealed class NativeDependencyResolver : IDependencyResolver
    {
        private readonly IServiceProvider _root;
        private readonly IHttpContextAccessor _accessor;

        public NativeDependencyResolver(IServiceProvider services)
        {
            _root = services ?? throw new ArgumentNullException(nameof(services));
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
        }

        private IServiceProvider Services => _accessor.HttpContext?.RequestServices ?? _root;

        public object GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return Services.GetService(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return Services.GetServices(serviceType);
        }
    }
}
