using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Routing.Constraints;
using NativeValues = Microsoft.AspNetCore.Routing.RouteValueDictionary;
using NativeDirection = Microsoft.AspNetCore.Routing.RouteDirection;

namespace System.Web.Routing;

// Retained System.Web public shape, hosted in this unsigned compatibility assembly.
// The native mutable list is always private; callers own no configuration reference.
public class HttpMethodConstraint : IRouteConstraint
{
    private readonly HttpMethodRouteConstraint _native;
    public HttpMethodConstraint(params string[] allowedMethods)
    {
        ArgumentNullException.ThrowIfNull(allowedMethods);
        AllowedMethods = Array.AsReadOnly(allowedMethods.ToArray());
        _native = new HttpMethodRouteConstraint(AllowedMethods.ToArray());
    }
    public ICollection<string> AllowedMethods { get; private set; }
    protected virtual bool Match(HttpContextBase httpContext, Route route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(parameterName);
        ArgumentNullException.ThrowIfNull(values);
        var direction = (NativeDirection)routeDirection;
        ValidateOutgoing(parameterName, new NativeValues(values), direction);
        // Public legacy callers retain their own HttpContextBase; hosted matching
        // uses the separate real-native-context bridge below.
        if (direction == NativeDirection.IncomingRequest)
            return AllowedMethods.Any(method => Microsoft.AspNetCore.Http.HttpMethods.Equals(method, httpContext.Request.HttpMethod));
        return _native.Match(null, null, parameterName, new NativeValues(values), direction);
    }
    bool IRouteConstraint.Match(HttpContextBase httpContext, Route route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection)
        => Match(httpContext, route, parameterName, values, routeDirection);
    internal static void ValidateOutgoing(string key, NativeValues values, NativeDirection direction)
    {
        if (direction == NativeDirection.UrlGeneration && values.TryGetValue(key, out var value) && value is not string)
            throw new InvalidOperationException("The HTTP method route value must be a string: " + key);
    }
}

internal sealed class NativeHttpMethodConstraint : Microsoft.AspNetCore.Routing.IRouteConstraint
{
    private readonly HttpMethodConstraint _source;
    private readonly string[] _methods;
    private readonly HttpMethodRouteConstraint _native;
    internal NativeHttpMethodConstraint(HttpMethodConstraint source)
    {
        if (source.GetType() != typeof(HttpMethodConstraint)) throw Route.Unavailable();
        _source = source; _methods = source.AllowedMethods.ToArray();
        _native = new HttpMethodRouteConstraint(_methods);
    }
    internal void Check()
    {
        if (!_source.AllowedMethods.SequenceEqual(_methods)) throw Route.Unavailable();
    }
    public bool Match(Microsoft.AspNetCore.Http.HttpContext context, Microsoft.AspNetCore.Routing.IRouter router,
        string key, NativeValues values, NativeDirection direction)
    {
        Check(); HttpMethodConstraint.ValidateOutgoing(key, values, direction);
        return _native.Match(context, router, key, values, direction);
    }
}
