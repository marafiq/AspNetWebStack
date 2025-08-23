using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

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
		AddToIndex("*|" + prefix, route);
		AddToIndex(route.Method + "|" + prefix, route);
	}

	public List<Route> GetCandidates(string method, string path)
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

[MemoryDiagnoser]
public class RoutingBenchmarks
{
	private MethodPrefixIndex _index = default!;
	private (string method, string path)[] _queries = default!;

	[GlobalSetup]
	public void Setup()
	{
		_index = new MethodPrefixIndex();
		var rnd = new Random(42);
		string[] methods = new[] { "GET", "POST", "PUT", "DELETE" };
		string[] nouns = Enumerable.Range(0, 500).Select(i => $"resource{i}").ToArray();

		// 5000 routes: mix of literal and parameter segments
		for (int i = 0; i < 5000; i++)
		{
			string m = methods[i % methods.Length];
			string a = nouns[rnd.Next(nouns.Length)];
			string b = nouns[rnd.Next(nouns.Length)];
			string tpl = (i % 3) switch
			{
				0 => $"api/{a}/{b}",
				1 => $"api/{a}/{{id}}",
				_ => $"{a}/{{controller}}/{{action}}/{{id}}"
			};
			_index.Add(new Route(m, tpl));
		}

		// 10k queries
		_queries = Enumerable.Range(0, 10_000).Select(i =>
		{
			string m = methods[i % methods.Length];
			string a = nouns[rnd.Next(nouns.Length)];
			string b = nouns[rnd.Next(nouns.Length)];
			string path = (i % 3) switch
			{
				0 => $"/api/{a}/{b}",
				1 => $"/api/{a}/{rnd.Next(1000)}",
				_ => $"/{a}/x/y/{rnd.Next(1000)}"
			};
			return (m, path);
		}).ToArray();
	}

	[Benchmark]
	public int LookupCandidates()
	{
		int total = 0;
		for (int i = 0; i < _queries.Length; i++)
		{
			var (m, p) = _queries[i];
			var cands = _index.GetCandidates(m, p);
			total += cands.Count;
		}
		return total;
	}
}

class Program
{
	static void Main(string[] args)
	{
		BenchmarkRunner.Run<RoutingBenchmarks>();
	}
}