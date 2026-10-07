using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.Mvc.Async;
using System.Web.Mvc.Routing;
using AspNetWebStack.Native;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NativeValues = Microsoft.AspNetCore.Routing.RouteValueDictionary;

namespace System.Web.Routing;

internal interface INativeRoutingContext { Microsoft.AspNetCore.Http.HttpContext CoreContext { get; } }

// Native endpoints own pattern parsing, matching, constraints and URL generation.
// The legacy collection supplies registration order and original MVC data tokens.
internal sealed class NativeRouteBinding
{
    private readonly RouteCollection _routes;
    private readonly Entry[] _entries;
    private readonly RouteBase[] _topRoutes;
    private readonly string[] _topNames;
    private readonly bool _lowercase, _trailing;
    private NativeRouteBinding(RouteCollection routes)
    {
        _routes = routes; _lowercase = routes.LowercaseUrls; _trailing = routes.AppendTrailingSlash;
        if (routes.RouteExistingFiles || routes.Count == 0) throw Route.Unavailable();
        _topRoutes = routes.ToArray(); _topNames = _topRoutes.Select(routes.NameFor).ToArray();
        var entries = new List<Entry>();
        foreach (var (route, order) in _topRoutes.Select((r, i) => (r, i)))
        {
            if (route is RouteCollectionRoute aggregate)
            {
                if (aggregate.RouteExistingFiles) throw Route.Unavailable();
                foreach (var child in aggregate) entries.Add(new Entry(this, child, null, order, aggregate));
            }
            else if (route is not LinkGenerationRoute) entries.Add(new Entry(this, route, routes.NameFor(route), order));
        }
        _entries = entries.ToArray();
        foreach (var wrapper in _topRoutes.OfType<LinkGenerationRoute>())
            if (!_entries.Any(e => e.Aggregate != null && ReferenceEquals(e.Route, wrapper.NativeInnerRoute))) throw Route.Unavailable();
        if (_entries.Select(entry => entry.Route).Distinct().Count() != _entries.Length) throw Route.Unavailable();
    }

    internal static void Bind(RouteCollection routes, IEndpointRouteBuilder endpoints, RequestDelegate dispatch, bool acceptForms = false)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(endpoints); ArgumentNullException.ThrowIfNull(dispatch);
        using (routes.GetWriteLock())
        {
            if (routes.NativeBinding != null) throw Route.Unavailable();
            var binding = new NativeRouteBinding(routes);
            if (binding._entries.Any(e => e.Aggregate != null) && !endpoints.ServiceProvider.GetServices<MatcherPolicy>().Any(p => p is NativeAttributeRoutePolicy))
                throw new InvalidOperationException("Call services.AddNativeMvcRouting() before building the service provider to enable MVC attribute routes.");
            var built = new List<Endpoint>();
            foreach (var entry in binding._entries)
            {
                var endpoint = new RouteEndpointBuilder(dispatch, entry.Pattern, entry.Order) { DisplayName = "MVC route: " + (entry.Name ?? entry.Url) };
                endpoint.Metadata.Add(new HttpMethodMetadata(acceptForms ? AspNetWebStack.Native.NativeRequestMethods.Application : new[] { "GET" }));
                endpoint.Metadata.Add(new EndpointNameMetadata(entry.Address));
                endpoint.Metadata.Add(entry); built.Add(endpoint.Build());
            }
            // Finish all configuration validation before publishing any endpoint.
            endpoints.DataSources.Add(new DefaultEndpointDataSource(built));
            routes.NativeBinding = binding;
            foreach (var entry in binding._entries) entry.Route.NativeBinding = binding;
            foreach (var wrapper in binding._topRoutes.OfType<LinkGenerationRoute>()) wrapper.NativeBinding = binding;
        }
    }

    internal void ValidateUnchanged()
    {
        if (_routes.Count != _topRoutes.Length || _routes.RouteExistingFiles || _routes.LowercaseUrls != _lowercase || _routes.AppendTrailingSlash != _trailing)
            throw Route.Unavailable();
        for (int i = 0; i < _topRoutes.Length; i++)
            if (!ReferenceEquals(_routes[i], _topRoutes[i]) || _routes.NameFor(_topRoutes[i]) != _topNames[i]) throw Route.Unavailable();
        foreach (var aggregate in _topRoutes.OfType<RouteCollectionRoute>())
            if (aggregate.RouteExistingFiles || !aggregate.SequenceEqual(_entries.Where(e => ReferenceEquals(e.Aggregate, aggregate)).Select(e => e.Route))) throw Route.Unavailable();
        foreach (var entry in _entries) entry.Check();
        foreach (var wrapper in _topRoutes.OfType<LinkGenerationRoute>())
        {
            var inner = _entries.Single(e => ReferenceEquals(e.Route, wrapper.NativeInnerRoute));
            inner.Check(wrapper);
        }
    }

    internal static Microsoft.AspNetCore.Http.HttpContext RequireContext(HttpContextBase context)
    {
        if (context is not INativeRoutingContext bridge) throw new PlatformNotSupportedException("Routing requires the owned native request context.");
        return bridge.CoreContext;
    }
    // Retained by earlier proof-host admission policies, not the general router.
    internal static bool IsIdentifier(string value) => !String.IsNullOrEmpty(value) && value.Length <= 64 && value.All(c => Char.IsAsciiLetterOrDigit(c) || c == '_');

    internal RouteData GetRouteData(HttpContextBase context, Route route = null)
    {
        ValidateUnchanged();
        var core = RequireContext(context);
        var entry = core.GetEndpoint()?.Metadata.GetMetadata<Entry>();
        if (entry == null || !_entries.Contains(entry)) return null;
        if (entry.Aggregate != null)
        {
            var selected = core.Features.Get<NativeDirectRouteMatches>();
            if (selected == null || !ReferenceEquals(selected.Aggregate, entry.Aggregate)) throw Route.Unavailable();
            if (route != null)
            {
                var match = selected.Matches.SingleOrDefault(m => ReferenceEquals(m.Entry.Route, route));
                return match == null ? null : CreateRouteData(match.Entry, match.Values);
            }
            return RouteCollectionRoute.CreateDirectRouteMatch(entry.Aggregate, selected.Matches.Select(m => CreateRouteData(m.Entry, m.Values)).ToList());
        }
        if (route != null && !ReferenceEquals(route, entry.Route)) return null;
        return CreateRouteData(entry, core.Request.RouteValues);
    }
    private static RouteData CreateRouteData(Entry entry, NativeValues values)
    {
        var result = new RouteData(entry.Route, entry.Route.RouteHandler);
        foreach (var pair in values) if (entry.Aggregate != null || pair.Value != UrlParameter.Optional) result.Values.Add(pair.Key, pair.Value);
        if (entry.Aggregate != null)
            foreach (var pair in entry.Defaults)
                if (pair.Value == UrlParameter.Optional && !result.Values.ContainsKey(pair.Key)) result.Values.Add(pair.Key, pair.Value);
        foreach (var pair in entry.Tokens) result.DataTokens.Add(pair.Key, Clone(pair.Value));
        return result;
    }

    internal VirtualPathData GetVirtualPath(RequestContext context, RouteValueDictionary values, Route route, bool applicationPath, RouteCollection options = null)
    {
        ArgumentNullException.ThrowIfNull(context); ValidateUnchanged();
        var target = route is LinkGenerationRoute wrapper ? wrapper.NativeInnerRoute : route;
        var entry = _entries.Single(candidate => ReferenceEquals(candidate.Route, target));
        var core = RequireContext(context.HttpContext);
        var explicitValues = Values(values); var ambient = Values(context.RouteData?.Values);
        var path = core.RequestServices.GetRequiredService<LinkGenerator>().GetPathByAddress(core, entry.Address, explicitValues, ambient,
            pathBase: applicationPath ? core.Request.PathBase : PathString.Empty,
            options: new LinkOptions { LowercaseUrls = applicationPath && (options?.LowercaseUrls ?? _lowercase), LowercaseQueryStrings = false, AppendTrailingSlash = applicationPath && (options?.AppendTrailingSlash ?? _trailing) });
        if (path == null) return null;
        var result = new VirtualPathData(route, applicationPath ? path : path.TrimStart('/'));
        foreach (var pair in entry.Tokens) result.DataTokens.Add(pair.Key, Clone(pair.Value));
        return result;
    }

    private static NativeValues Values(IEnumerable<KeyValuePair<string, object>> source)
    {
        var values = new NativeValues();
        if (source != null) foreach (var pair in source)
        {
            // Original child binding uses an internal provider token, never a URL value.
            if (pair.Key == ChildActionValueProvider.ChildActionValuesKey || pair.Value == UrlParameter.Optional) continue;
            if (!Scalar(pair.Value)) throw new PlatformNotSupportedException("Route values must be immutable scalar values.");
            values.Add(pair.Key, pair.Value);
        }
        return values;
    }
    private static bool Scalar(object value) => value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal || value is Guid || value is DateTime || value is DateTimeOffset;
    private static object Clone(object value) => value is string[] array ? array.ToArray() : value is ActionDescriptor[] actions ? actions.ToArray() : value;
    private static RouteValueDictionary Snapshot(RouteValueDictionary source, bool tokens = false, bool direct = false, bool constraints = false)
    {
        var copy = new RouteValueDictionary();
        if (source != null) foreach (var pair in source)
        {
            if (direct && tokens && pair.Key == RouteDataTokenKeys.Actions && pair.Value is ActionDescriptor[] actions)
            {
                if (actions.Length == 0 || actions.Any(a => a == null || (a.GetType() != typeof(ReflectedActionDescriptor) && a.GetType() != typeof(ReflectedAsyncActionDescriptor) && a.GetType() != typeof(TaskAsyncActionDescriptor)) || a.ControllerDescriptor.GetType() != typeof(ReflectedAsyncControllerDescriptor))) throw Route.Unavailable();
            }
            else if (constraints && pair.Value?.GetType() == typeof(HttpMethodConstraint)) { }
            else if (constraints && pair.Value is IRouteConstraint constraint) _ = NativeStandardRouteConstraint.Snapshot(constraint, out _);
            else if (!Scalar(pair.Value) && pair.Value != UrlParameter.Optional && !(tokens && pair.Value is string[])) throw Route.Unavailable();
            copy.Add(pair.Key, Clone(pair.Value));
        }
        return copy;
    }
    private static bool Same(RouteValueDictionary source, RouteValueDictionary snapshot) =>
        (source?.Count ?? 0) == snapshot.Count && snapshot.All(pair => source.TryGetValue(pair.Key, out var value) &&
            (pair.Value is string[] array ? value is string[] other && array.SequenceEqual(other) : pair.Value is ActionDescriptor[] actions ? value is ActionDescriptor[] otherActions && actions.SequenceEqual(otherActions) : Equals(pair.Value, value)));

    // Endpoint TemplateBinder omits an absent catch-all before evaluating its
    // policy. Framework evaluates the immutable Optional default in that case.
    // Restore only that input to native regex matching; native URL binding stays
    // responsible for all output and ambient/default selection.
    private sealed class OptionalCatchAllConstraint : Microsoft.AspNetCore.Routing.IRouteConstraint
    {
        private readonly RegexRouteConstraint _regex;
        internal OptionalCatchAllConstraint(RegexRouteConstraint regex) { _regex = regex; }
        public bool Match(Microsoft.AspNetCore.Http.HttpContext context, IRouter router, string key, NativeValues values, Microsoft.AspNetCore.Routing.RouteDirection direction)
        {
            if (direction == Microsoft.AspNetCore.Routing.RouteDirection.UrlGeneration && !values.ContainsKey(key))
            {
                values = new NativeValues(values); values.Add(key, UrlParameter.Optional);
            }
            return _regex.Match(context, router, key, values, direction);
        }
    }

    internal sealed class Entry
    {
        internal readonly NativeRouteBinding Binding;
        internal readonly RouteCollectionRoute Aggregate;
        private readonly List<NativeStandardRouteConstraint> _standardConstraints = new();
        private readonly List<NativeHttpMethodConstraint> _methodConstraints = new();
        internal readonly Route Route;
        internal readonly string Name, Url, Address = "mvc-" + Guid.NewGuid().ToString("N");
        internal readonly int Order;
        internal readonly RouteValueDictionary Defaults, Constraints, Tokens;
        internal readonly RoutePattern Pattern;
        private readonly IRouteHandler _handler;
        internal Entry(NativeRouteBinding binding, RouteBase source, string name, int order, RouteCollectionRoute aggregate = null)
        {
            if (source.GetType() != typeof(Route)) throw Route.Unavailable();
            Binding = binding; Aggregate = aggregate; Route = (Route)source;
            if (Route.NativeBinding != null || Route.RouteExistingFiles || (Route.RouteHandler != null &&
                (Route.RouteHandler.GetType() != typeof(MvcRouteHandler) || !((MvcRouteHandler)Route.RouteHandler).NativeUsesDefaultFactory))) throw Route.Unavailable();
            Name = name; Order = order; Url = Route.Url; _handler = Route.RouteHandler;
            Defaults = Snapshot(Route.Defaults); Tokens = Snapshot(Route.DataTokens, tokens: true, direct: aggregate != null);
            Constraints = Snapshot(Route.Constraints, constraints: true);
            var parsed = RoutePatternFactory.Parse(Url);
            // Conventional MVC pattern syntax plus native parsing/encoding. The
            // legacy optional sentinel is translated into native optional parts.
            if (parsed.Parameters.Any(p => p.IsOptional || p.Default != null || p.ParameterPolicies.Count != 0))
                throw new PlatformNotSupportedException("Use conventional defaults/UrlParameter.Optional and the constraints argument, not inline native policies.");
            var defaults = Values(Defaults); var policies = new NativeValues();
            // Framework constraints evaluate the empty Optional sentinel. A native
            // optional parameter would skip its policy, so retain the immutable legacy sentinel as a native default
            // for constrained Optional names and remove it before MVC binding.
            foreach (var pair in Defaults.Where(pair => pair.Value == UrlParameter.Optional && Constraints.ContainsKey(pair.Key)))
                defaults.Add(pair.Key, UrlParameter.Optional);
            foreach (var constraint in Constraints)
            {
                // Native DFA matching and LinkGenerator both evaluate nonparameter
                // policies. A method miss stays a route miss, never per-route 405.
                if (constraint.Value?.GetType() == typeof(HttpMethodConstraint))
                {
                    var method = new NativeHttpMethodConstraint((HttpMethodConstraint)constraint.Value);
                    _methodConstraints.Add(method); policies.Add(constraint.Key, method); continue;
                }
                if (!parsed.Parameters.Any(p => String.Equals(p.Name, constraint.Key, StringComparison.OrdinalIgnoreCase))) throw Route.Unavailable();
                if (constraint.Value is IRouteConstraint original)
                {
                    var bridge = new NativeStandardRouteConstraint(original, Route); _standardConstraints.Add(bridge); policies.Add(constraint.Key, bridge); continue;
                }
                if (constraint.Value is not string regex) throw Route.Unavailable();
                var policy = new RegexRouteConstraint("^(" + regex + ")$");
                _ = policy.Constraint; // Validate regex syntax at startup, before publishing the table.
                policies.Add(constraint.Key, Defaults[constraint.Key] == UrlParameter.Optional && parsed.Parameters.Any(p => String.Equals(p.Name, constraint.Key, StringComparison.OrdinalIgnoreCase) && p.IsCatchAll)
                    ? new OptionalCatchAllConstraint(policy) : policy);
            }
            foreach (var parameter in parsed.Parameters.Where(parameter => Defaults[parameter.Name] == UrlParameter.Optional && !parameter.IsCatchAll))
                if (parsed.PathSegments[^1].Parts.Count != 1 || !ReferenceEquals(parsed.PathSegments[^1].Parts[0], parameter))
                    throw new PlatformNotSupportedException("An optional conventional parameter must be the final complete segment.");
            var segments = parsed.PathSegments.Select(segment => RoutePatternFactory.Segment(segment.Parts.Select(part =>
                part is RoutePatternParameterPart parameter
                    ? parameter.IsCatchAll ? RoutePatternFactory.Parse("{**" + parameter.Name + "}").Parameters[0]
                    : Defaults[parameter.Name] == UrlParameter.Optional && !Constraints.ContainsKey(parameter.Name) ? RoutePatternFactory.ParameterPart(parameter.Name, null, RoutePatternParameterKind.Optional) : part
                    : part))).ToArray();
            Pattern = RoutePatternFactory.Pattern(Url, defaults, policies, segments);
        }
        internal void Check(Route source = null)
        {
            source ??= Route;
            if (source.Url != Url || source.RouteExistingFiles || !ReferenceEquals(source.RouteHandler, _handler) ||
                !Same(source.Defaults, Defaults) || !Same(source.Constraints, Constraints) || !Same(source.DataTokens, Tokens)) throw Route.Unavailable();
            foreach (var constraint in _standardConstraints) constraint.Check();
            foreach (var constraint in _methodConstraints) constraint.Check();
        }
    }
}
