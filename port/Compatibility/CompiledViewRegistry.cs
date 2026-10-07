using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Web.Mvc;
using System.Web;
using System.Web.WebPages;

namespace AspNetWebStack.Native
{
    // Native startup configuration, separate from the original MVC API. The
    // immutable snapshot holds only types, never requests or activated pages.
    public sealed class CompiledViewRegistry
    {
        private readonly Dictionary<string, Type> _types;

        public CompiledViewRegistry(IEnumerable<KeyValuePair<string, Type>> compiledTypes)
            : this(compiledTypes, StringComparer.Ordinal)
        {
        }

        private CompiledViewRegistry(IEnumerable<KeyValuePair<string, Type>> compiledTypes, StringComparer comparer)
        {
            if (compiledTypes == null) throw new ArgumentNullException("compiledTypes");
            _types = new Dictionary<string, Type>(comparer);
            foreach (var entry in compiledTypes)
            {
                ValidatePath(entry.Key);
                if (entry.Value == null) throw new ArgumentException("A compiled registration requires a type.", "compiledTypes");
                _types.Add(entry.Key, entry.Value);
            }
        }

        public static CompiledViewRegistry FromAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return new CompiledViewRegistry(assembly.GetCustomAttributes<CompiledRazorViewAttribute>()
                .Select(attribute => new KeyValuePair<string, Type>(attribute.VirtualPath, attribute.ViewType)));
        }

        public static CompiledViewRegistry FromAssemblies(IEnumerable<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            var snapshot = assemblies.ToArray();
            if (snapshot.Any(assembly => assembly == null))
                throw new ArgumentException("Application assemblies cannot contain null.", nameof(assemblies));
            return new CompiledViewRegistry(snapshot.Distinct()
                .SelectMany(assembly => assembly.GetCustomAttributes<CompiledRazorViewAttribute>())
                .Select(attribute => new KeyValuePair<string, Type>(attribute.VirtualPath, attribute.ViewType)));
        }

        // Conventional MVC paths follow the original case-insensitive hosting
        // assumption; reject collisions at startup. Direct U22 registrations stay ordinal.
        internal CompiledViewRegistry ForConventionalLookup() => new CompiledViewRegistry(_types, StringComparer.OrdinalIgnoreCase);
        internal bool Contains(string path, ControllerContext context) => path != null && _types.ContainsKey(CanonicalPath(path, context));
        private static string CanonicalPath(string path, ControllerContext context)
        {
            return path != null && path.StartsWith("/", StringComparison.Ordinal)
                ? VirtualPathUtility.ToAppRelative(path, context.HttpContext.Request.ApplicationPath) ?? String.Empty : path;
        }
        internal RazorView Configure(RazorView view, ControllerContext controllerContext)
        {
            Func<string, string> normalize = path => CanonicalPath(path, controllerContext);
            view.BuildManager = new RegisteredBuildManager(_types, normalize);
            view.VirtualPathFactory = new RegisteredPathFactory(_types, controllerContext, view.ViewPageActivator, normalize);
            return view;
        }

        public RazorView CreateView(ControllerContext controllerContext, string viewPath, string layoutPath,
            bool runViewStartPages, IEnumerable<string> viewStartFileExtensions,
            IViewPageActivator viewPageActivator = null, IVirtualPathFactory virtualPathFactory = null)
        {
            var view = new RazorView(controllerContext, viewPath, layoutPath, runViewStartPages, viewStartFileExtensions, viewPageActivator);
            view.BuildManager = new RegisteredBuildManager(_types);
            view.VirtualPathFactory = virtualPathFactory ?? new RegisteredPathFactory(_types, controllerContext, view.ViewPageActivator);
            return view;
        }

        private static void ValidatePath(string path)
        {
            if (String.IsNullOrEmpty(path) || !path.StartsWith("~/", StringComparison.Ordinal) || path.Length == 2 ||
                path.IndexOfAny(new[] { '\\', '?', '#', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Compiled paths must be canonical application-relative identifiers.", "compiledTypes");
            foreach (var segment in path.Substring(2).Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new ArgumentException("Compiled paths cannot contain empty or relative segments.", "compiledTypes");
        }

        private sealed class RegisteredBuildManager : IBuildManager
        {
            private readonly Dictionary<string, Type> _types;
            private readonly Func<string, string> _normalize;
            internal RegisteredBuildManager(Dictionary<string, Type> types, Func<string, string> normalize = null) { _types = types; _normalize = normalize ?? (path => path); }
            public bool FileExists(string path) { return path != null && _types.ContainsKey(_normalize(path)); }
            public Type GetCompiledType(string path) { Type type; return path != null && _types.TryGetValue(_normalize(path), out type) ? type : null; }
            public ICollection GetReferencedAssemblies() { throw Unavailable(); }
            public Stream ReadCachedFile(string path) { throw Unavailable(); }
            public Stream CreateCachedFile(string path) { throw Unavailable(); }
            private static Exception Unavailable() { return new PlatformNotSupportedException("Compiled registrations do not implement BuildManager assembly discovery or file caches."); }
        }

        private sealed class RegisteredPathFactory : IVirtualPathFactory
        {
            private readonly Dictionary<string, Type> _types;
            private readonly ControllerContext _context;
            private readonly IViewPageActivator _activator;
            private readonly Func<string, string> _normalize;
            internal RegisteredPathFactory(Dictionary<string, Type> types, ControllerContext context, IViewPageActivator activator, Func<string, string> normalize = null)
            { _types = types; _context = context; _activator = activator; _normalize = normalize ?? (path => path); }
            public bool Exists(string path) { return path != null && _types.ContainsKey(_normalize(path)); }
            public object CreateInstance(string path)
            {
                Type type;
                if (path == null || !_types.TryGetValue(_normalize(path), out type)) throw new InvalidOperationException("The compiled virtual path is not registered.");
                var instance = _activator.Create(_context, type);
                var page = instance as WebPageBase;
                if (page != null) { page.VirtualPath = path; page.VirtualPathFactory = this; }
                return instance;
            }
        }
    }
}
