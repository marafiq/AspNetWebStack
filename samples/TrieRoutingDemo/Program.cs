using System;
using System.Collections.Generic;

class Route
{
	public string Method { get; }
	public string Template { get; }
	public Route(string method, string template)
	{
		Method = method.ToUpperInvariant();
		Template = template.Trim('/');
	}
}

class MethodPrefixIndex
{
	private readonly Dictionary<string, List<Route>> _index = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<Route> _paramFirst = new();

	public void Add(Route route)
	{
		string prefix = GetLeadingLiteralPrefix(route.Template);
		if (prefix.Length == 0)
		{
			_paramFirst.Add(route);
			return;
		}

		string anyKey = "*|" + prefix;
		string methodKey = route.Method + "|" + prefix;
		AddToIndex(anyKey, route);
		AddToIndex(methodKey, route);
	}

	public IEnumerable<Route> GetCandidates(string method, string path)
	{
		List<Route> candidates = new();
		method = (method ?? "").ToUpperInvariant();
		path = (path ?? "").Trim('/');
		var segs = path.Length == 0 ? Array.Empty<string>() : path.Split('/');

		for (int len = segs.Length; len >= 0; len--)
		{
			string prefix = Join(segs, len);
			TryAdd(candidates, method + "|" + prefix);
			TryAdd(candidates, "*|" + prefix);
			if (candidates.Count > 0) break;
		}

		if (_paramFirst.Count > 0) candidates.AddRange(_paramFirst);
		return candidates;
	}

	private void TryAdd(List<Route> list, string key)
	{
		if (_index.TryGetValue(key, out var bucket)) list.AddRange(bucket);
	}

	private void AddToIndex(string key, Route route)
	{
		if (!_index.TryGetValue(key, out var bucket))
		{
			bucket = new List<Route>();
			_index[key] = bucket;
		}
		bucket.Add(route);
	}

	private static string GetLeadingLiteralPrefix(string template)
	{
		if (string.IsNullOrEmpty(template)) return string.Empty;
		var parts = template.Split('/');
		List<string> lits = new();
		foreach (var p in parts)
		{
			if (string.IsNullOrEmpty(p)) continue;
			if (p.Contains("{")) break;
			lits.Add(p);
		}
		return lits.Count == 0 ? string.Empty : string.Join("/", lits);
	}

	private static string Join(string[] parts, int len)
	{
		if (len <= 0) return string.Empty;
		if (len == 1) return parts[0];
		if (len > parts.Length) len = parts.Length;
		return string.Join("/", parts, 0, len);
	}
}

class Program
{
	static void Main()
	{
		var idx = new MethodPrefixIndex();
		var routes = new[]
		{
			new Route("GET", "api/products/{id}"),
			new Route("POST", "api/products"),
			new Route("GET", "api/orders/{orderId}/items/{itemId}"),
			new Route("GET", "health"),
			new Route("GET", "{controller}/{action}/{id}") // catch-all param-first
		};
		foreach (var r in routes) idx.Add(r);

		RunCase(idx, "GET", "/api/products/123");
		RunCase(idx, "POST", "/api/products");
		RunCase(idx, "GET", "/api/orders/9/items/42");
		RunCase(idx, "GET", "/health");
		RunCase(idx, "GET", "/foo/bar/9");
	}

	static void RunCase(MethodPrefixIndex idx, string method, string path)
	{
		Console.WriteLine($"Request {method} {path}");
		int n = 0;
		foreach (var c in idx.GetCandidates(method, path))
		{
			Console.WriteLine($"  cand[{n++}] => {c.Method} {c.Template}");
		}
		Console.WriteLine();
	}
}