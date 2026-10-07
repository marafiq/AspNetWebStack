using System;
using System.Web;
using System.Web.Mvc;
using System.Web.WebPages;
using Microsoft.Extensions.Caching.Memory;

namespace AspNetWebStack.Native
{
    // Only hosting boundaries are supplied here. Original MVC owns lookup,
    // namespace/area conventions, cached lookup, view construction and release.
    public sealed class NativeCompiledRazorViewEngine : RazorViewEngine, IDisposable
    {
        private readonly CompiledViewRegistry _registry;
        private readonly NativeLocations _ownedCache;
        private bool _disposed;

        public NativeCompiledRazorViewEngine(CompiledViewRegistry registry, IViewPageActivator activator = null,
            IViewLocationCache cache = null) : base(activator)
        {
            ArgumentNullException.ThrowIfNull(registry);
            _registry = registry.ForConventionalLookup();
            FileExtensions = new[] { "cshtml" };
            DisplayModeProvider = new DisplayModeProvider();
            DisplayModeProvider.Modes.Clear();
            DisplayModeProvider.Modes.Add(new DefaultDisplayMode());
            // Browser capability emulation is not configured; normal views only.
            if (cache == null) cache = _ownedCache = new NativeLocations();
            ViewLocationCache = cache;
        }
        protected override bool FileExists(ControllerContext context, string path)
        { Check(); return _registry.Contains(path, context); }
        protected override IView CreateView(ControllerContext context, string path, string master)
        { Check(); return _registry.Configure((RazorView)base.CreateView(context, path, master), context); }
        protected override IView CreatePartialView(ControllerContext context, string path)
        { Check(); return _registry.Configure((RazorView)base.CreatePartialView(context, path), context); }
        public override ViewEngineResult FindView(ControllerContext context, string name, string master, bool useCache)
        { Check(); return base.FindView(context, name, master, useCache); }
        public override ViewEngineResult FindPartialView(ControllerContext context, string name, bool useCache)
        { Check(); return base.FindPartialView(context, name, useCache); }
        private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
        public void Dispose() { if (!_disposed) { _disposed = true; _ownedCache?.Dispose(); } }

        private sealed class NativeLocations : IViewLocationCache, IDisposable
        {
            private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
            public string GetViewLocation(HttpContextBase context, string key) => _cache.TryGetValue(key, out string path) ? path : null;
            public void InsertViewLocation(HttpContextBase context, string key, string path)
                => _cache.Set(key, path, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(15) });
            public void Dispose() => _cache.Dispose();
        }
    }
}
