using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Async;
using System.Web.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using C = System.Web.Mvc.Routing.Constraints;
using Legacy = System.Web.Routing;

namespace AspNetWebStack.Native;

public static class NativeMvcRoutingServices
{
    // Register before building the native service provider. Map never mutates DI.
    public static IServiceCollection AddNativeMvcRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRouting();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<MatcherPolicy, NativeAttributeRoutePolicy>());
        return services;
    }
}

internal sealed class NativeAttributeRoutePolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    // Host-wide method admission runs first. No action-specific method metadata
    // is published: original MVC must see all direct matches, including verbs
    // that later fail and cross-controller matches that are ambiguous.
    public override int Order => -900;
    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => endpoints.Any(e => e.Metadata.GetMetadata<Legacy.NativeRouteBinding.Entry>()?.Aggregate != null);
    public Task ApplyAsync(HttpContext context, CandidateSet candidates)
    {
        var entries = new List<(int Index, Legacy.NativeRouteBinding.Entry Entry)>();
        for (int i = 0; i < candidates.Count; i++)
            if (candidates.IsValidCandidate(i) && candidates[i].Endpoint.Metadata.GetMetadata<Legacy.NativeRouteBinding.Entry>() is { } entry)
                entries.Add((i, entry));
        foreach (var group in entries.GroupBy(e => e.Entry.Binding))
        {
            group.Key.ValidateUnchanged();
            int order = group.Min(e => e.Entry.Order);
            var first = group.First(e => e.Entry.Order == order);
            if (first.Entry.Aggregate == null) continue;
            var matches = group.Where(e => ReferenceEquals(e.Entry.Aggregate, first.Entry.Aggregate))
                .Select(e => new NativeDirectRouteMatch(e.Entry, new RouteValueDictionary(candidates[e.Index].Values))).ToArray();
            context.Features.Set(new NativeDirectRouteMatches(first.Entry.Aggregate, matches));
            foreach (var item in group)
                if (item.Index != first.Index) candidates.SetValidity(item.Index, false);
        }
        return Task.CompletedTask;
    }
}

internal sealed record NativeDirectRouteMatch(Legacy.NativeRouteBinding.Entry Entry, RouteValueDictionary Values);
internal sealed record NativeDirectRouteMatches(RouteCollectionRoute Aggregate, NativeDirectRouteMatch[] Matches);

internal sealed class NativeStandardRouteConstraint : Microsoft.AspNetCore.Routing.IRouteConstraint
{
    private readonly Legacy.IRouteConstraint _constraint;
    private readonly Legacy.Route _route;
    private readonly Action _check;
    internal NativeStandardRouteConstraint(Legacy.IRouteConstraint constraint, Legacy.Route route)
    {
        _constraint = Snapshot(constraint, out _check);
        _route = new Legacy.Route(route.Url, new Legacy.RouteValueDictionary(route.Defaults ?? new Legacy.RouteValueDictionary()), null);
    }
    internal static Legacy.IRouteConstraint Snapshot(Legacy.IRouteConstraint constraint, out Action check, int depth = 0)
    {
        if (constraint == null || depth > 32) throw Legacy.Route.Unavailable();
        var type = constraint.GetType();
        if (type == typeof(C.CompoundRouteConstraint))
        {
            var compound = (C.CompoundRouteConstraint)constraint;
            var originals = compound.Constraints.ToArray(); var checks = new List<Action>();
            var frozen = originals.Select(c => { var copy = Snapshot(c, out var validate, depth + 1); checks.Add(validate); return copy; }).ToArray();
            check = () => { if (!compound.Constraints.SequenceEqual(originals)) throw Legacy.Route.Unavailable(); foreach (var validate in checks) validate(); };
            return new C.CompoundRouteConstraint(frozen);
        }
        if (type == typeof(C.OptionalRouteConstraint))
        {
            var child = Snapshot(((C.OptionalRouteConstraint)constraint).InnerConstraint, out check, depth + 1);
            return new C.OptionalRouteConstraint(child);
        }
        // Exact original types only; these expose no mutable public state and
        // never dereference HttpContext. Custom subclasses/callbacks fail at Map.
        Type[] allowed = { typeof(C.AlphaRouteConstraint), typeof(C.BoolRouteConstraint), typeof(C.DateTimeRouteConstraint),
            typeof(C.DecimalRouteConstraint), typeof(C.DoubleRouteConstraint), typeof(C.FloatRouteConstraint), typeof(C.GuidRouteConstraint),
            typeof(C.IntRouteConstraint), typeof(C.LongRouteConstraint), typeof(C.LengthRouteConstraint), typeof(C.MaxLengthRouteConstraint),
            typeof(C.MinLengthRouteConstraint), typeof(C.MaxRouteConstraint), typeof(C.MinRouteConstraint), typeof(C.RangeRouteConstraint), typeof(C.RegexRouteConstraint) };
        if (!allowed.Contains(type)) throw new PlatformNotSupportedException("Native routing accepts original standard constraints only; custom constraint callbacks require a separate owned-context profile.");
        check = () => { }; return constraint;
    }
    internal void Check() => _check();
    public bool Match(HttpContext context, IRouter router, string key, RouteValueDictionary values, Microsoft.AspNetCore.Routing.RouteDirection direction)
    {
        Check();
        var copy = new Legacy.RouteValueDictionary(values);
        if (!copy.ContainsKey(key) && _route.Defaults[key] == UrlParameter.Optional) copy.Add(key, UrlParameter.Optional);
        return _constraint.Match(null, _route, key, copy, direction == Microsoft.AspNetCore.Routing.RouteDirection.IncomingRequest ? Legacy.RouteDirection.IncomingRequest : Legacy.RouteDirection.UrlGeneration);
    }
}
