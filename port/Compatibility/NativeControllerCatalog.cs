using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Mvc;

namespace AspNetWebStack.Native
{
    // Startup metadata only. The original type cache and namespace lookup own
    // discovery; each application catalog gets an isolated cache and builder.
    public sealed class NativeControllerCatalog
    {
        internal ControllerDescriptorCache Descriptors { get; } = new ControllerDescriptorCache();
        private readonly ControllerTypeCache _cache = new ControllerTypeCache();
        private readonly ControllerBuilder _builder = new ControllerBuilder();
        private readonly IBuildManager _assemblies;

        public NativeControllerCatalog(IEnumerable<Assembly> assemblies, IEnumerable<string> defaultNamespaces = null)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            var snapshot = assemblies.ToArray();
            if (snapshot.Any(assembly => assembly == null))
                throw new ArgumentException("Application assemblies cannot contain null.", nameof(assemblies));
            _assemblies = new AssemblyBuildManager(snapshot.Distinct().ToArray());
            _builder.DefaultNamespaces.UnionWith(defaultNamespaces ?? ControllerBuilder.Current.DefaultNamespaces);
        }

        internal void RegisterAreas(System.Web.Routing.RouteCollection routes, object state) => AreaRegistration.RegisterAllAreas(routes, _assemblies, state);

        internal IReadOnlyList<Type> GetControllerTypes() => CreateFactory().GetControllerTypes();

        public DefaultControllerFactory CreateFactory(IControllerActivator controllerActivator = null)
        {
            return new DefaultControllerFactory(controllerActivator)
            {
                BuildManager = _assemblies,
                ControllerTypeCache = _cache,
                ControllerBuilder = _builder
            };
        }

        private sealed class AssemblyBuildManager : IBuildManager
        {
            private readonly ICollection _assemblies;
            internal AssemblyBuildManager(Assembly[] assemblies) { _assemblies = Array.AsReadOnly(assemblies); }
            public ICollection GetReferencedAssemblies() { return _assemblies; }
            // Original TypeCacheUtil treats disk-cache failures as misses and
            // enumerates the supplied assemblies. No ASP.NET disk cache is faked.
            public Stream ReadCachedFile(string name) { throw Unavailable(); }
            public Stream CreateCachedFile(string name) { throw Unavailable(); }
            public bool FileExists(string path) { throw Unavailable(); }
            public Type GetCompiledType(string path) { throw Unavailable(); }
            private static Exception Unavailable() { return new PlatformNotSupportedException("The controller assembly catalog does not provide compilation or disk-cache services."); }
        }
    }
}
