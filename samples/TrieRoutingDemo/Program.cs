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

sealed class ZeroAllocIndex
{
	private sealed class NodeBuilder
	{
		public Dictionary<string, NodeBuilder> Children = new(StringComparer.OrdinalIgnoreCase);
		public List<Route> AnyRoutes = new();
		public Dictionary<string, List<Route>> MethodRoutes = new(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class Node
	{
		public SegmentEntry[] Children = Array.Empty<SegmentEntry>();
		public Route[] AnyRoutes = Array.Empty<Route>();
		public Dictionary<string, Route[]> MethodRoutes = new(StringComparer.OrdinalIgnoreCase);
	}

	private readonly NodeBuilder _rootBuilder = new();
	private Node? _root;
	private Route[] _paramFirstAny = Array.Empty<Route>();
	private Dictionary<string, Route[]> _paramFirstByMethod = new(StringComparer.OrdinalIgnoreCase);

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

	public void Add(Route route)
	{
		string prefix = GetLeadingLiteralPrefix(route.Template);
		if (prefix.Length == 0)
		{
			// parameter-first fallback
			if (!_paramFirstByMethod.TryGetValue(route.Method, out var list))
			{
				_paramFirstByMethod[route.Method] = list = Array.Empty<Route>();
			}
			// accumulate using builder local list then freeze later
			// for simplicity, reuse root builder method routes entry "" key
			if (!_rootBuilder.MethodRoutes.TryGetValue($"param:{route.Method}", out var l))
			{
				l = new List<Route>();
				_rootBuilder.MethodRoutes[$"param:{route.Method}"] = l;
			}
			l.Add(route);
			_rootBuilder.AnyRoutes.Add(route); // track for any as well
			return;
		}

		var segs = prefix.Split('/');
		var cur = _rootBuilder;
		for (int i = 0; i < segs.Length; i++)
		{
			string seg = segs[i];
			if (!cur.Children.TryGetValue(seg, out var next))
			{
				next = new NodeBuilder();
				cur.Children[seg] = next;
			}
			cur = next;
		}
		cur.AnyRoutes.Add(route);
		if (!cur.MethodRoutes.TryGetValue(route.Method, out var mr))
		{
			mr = new List<Route>();
			cur.MethodRoutes[route.Method] = mr;
		}
		mr.Add(route);
	}

	public void Freeze()
	{
		_root = FreezeNode(_rootBuilder);
		// freeze param-first buckets
		_paramFirstAny = _rootBuilder.AnyRoutes.ToArray();
		foreach (var kvp in _rootBuilder.MethodRoutes)
		{
			if (kvp.Key.StartsWith("param:", StringComparison.Ordinal))
			{
				string method = kvp.Key.Substring(6);
				_paramFirstByMethod[method] = kvp.Value.ToArray();
			}
		}
	}

	private static Node FreezeNode(NodeBuilder b)
	{
		var n = new Node
		{
			AnyRoutes = b.AnyRoutes.ToArray(),
			MethodRoutes = b.MethodRoutes.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
		};
		if (b.Children.Count == 0) return n;
		var entries = new List<SegmentEntry>(b.Children.Count);
		foreach (var kv in b.Children)
		{
			entries.Add(new SegmentEntry(kv.Key, FreezeNode(kv.Value)));
		}
		n.Children = entries.ToArray();
		return n;
	}

	public int CountCandidates(string method, string path)
	{
		if (_root == null) throw new InvalidOperationException("Freeze first");
		method = (method ?? string.Empty).ToUpperInvariant();
		ReadOnlySpan<char> s = (path ?? string.Empty).AsSpan().Trim('/');

		Node? node = _root;
		Node? best = null;
		int i = 0;
		while (i <= s.Length && node != null)
		{
			best = node;
			if (i == s.Length) break;
			int nextSlash = s.Slice(i).IndexOf('/');
			ReadOnlySpan<char> seg = nextSlash >= 0 ? s.Slice(i, nextSlash) : s.Slice(i);
			i = nextSlash >= 0 ? i + nextSlash + 1 : s.Length;
			node = FindChild(node, seg);
		}

		int count = 0;
		if (best != null)
		{
			count += best.AnyRoutes.Length;
			if (best.MethodRoutes.TryGetValue(method, out var mr)) count += mr.Length;
		}
		// param-first
		count += _paramFirstAny.Length;
		if (_paramFirstByMethod.TryGetValue(method, out var pm)) count += pm.Length;
		return count;
	}

	private static Node? FindChild(Node node, ReadOnlySpan<char> seg)
	{
		var children = node.Children;
		for (int k = 0; k < children.Length; k++)
		{
			if (seg.Equals(children[k].Segment, StringComparison.OrdinalIgnoreCase))
			{
				return children[k].Child;
			}
		}
		return null;
	}

	private readonly struct SegmentEntry
	{
		public readonly string Segment;
		public readonly Node Child;
		public SegmentEntry(string segment, Node child)
		{
			Segment = segment;
			Child = child;
		}
	}
}

[MemoryDiagnoser]
public class RoutingBenchmarks
{
	private ZeroAllocIndex _index = default!;
	private (string method, string path)[] _queries = default!;

	[GlobalSetup]
	public void Setup()
	{
		_index = new ZeroAllocIndex();
		var rnd = new Random(42);
		string[] methods = new[] { "GET", "POST", "PUT", "DELETE" };
		string[] nouns = Enumerable.Range(0, 500).Select(i => $"resource{i}").ToArray();

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
		_index.Freeze();

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
	public int LookupCandidates_ZeroAlloc()
	{
		int total = 0;
		for (int i = 0; i < _queries.Length; i++)
		{
			var (m, p) = _queries[i];
			total += _index.CountCandidates(m, p);
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