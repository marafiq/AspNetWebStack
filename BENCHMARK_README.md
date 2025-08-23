# ASP.NET Routing Performance Benchmark

## Overview

This benchmark tests the performance of ASP.NET routing systems with 5000 routes, targeting sub-1ms response times. The benchmark includes multiple implementations to demonstrate various optimization techniques.

## Performance Target

**Goal**: Achieve average route matching time of **less than 1ms** with 5000 routes.

## Benchmark Implementations

### 1. Standard Routing Benchmark (`RoutingBenchmark.cs`)
- Tests the default ASP.NET MVC and Web API routing
- Provides baseline performance measurements
- Uses standard `RouteCollection` and `HttpRouteCollection`

### 2. Optimized Routing Benchmark (`OptimizedRoutingBenchmark.cs`)
- Implements performance optimizations:
  - Compiled regex patterns
  - Route priority ordering
  - Route result caching
  - Optimized data structures
- Targets sub-1ms performance

### 3. Trie-Based Routing Benchmark (`TrieBasedRouting.cs`)
- Uses prefix tree (trie) data structure
- Logarithmic time complexity O(log n)
- Hierarchical route organization
- Targets sub-0.5ms performance

## Key Optimizations

### Route Caching
```csharp
private readonly ConcurrentDictionary<string, OptimizedRoute> _routeCache;
```

### Compiled Regex Patterns
```csharp
CompiledRegex = new Regex(pattern, RegexOptions.Compiled)
```

### Priority-Based Route Ordering
```csharp
_routes.Sort((a, b) => b.Priority.CompareTo(a.Priority));
```

### Trie Data Structure
```csharp
public class TrieNode
{
    public Dictionary<string, TrieNode> Children { get; set; }
    public List<OptimizedRoute> Routes { get; set; }
}
```

## Running the Benchmark

### Prerequisites
- .NET Framework 4.7.2 or later
- .NET SDK

### Quick Start
```bash
# Make the script executable
chmod +x run-benchmark.sh

# Run all benchmarks
./run-benchmark.sh
```

### Manual Execution
```bash
# Restore packages
dotnet restore

# Build the project
dotnet build --configuration Release

# Run specific benchmarks
dotnet run --configuration Release --project RoutingBenchmark.csproj
dotnet run --configuration Release --project OptimizedRoutingBenchmark.csproj
dotnet run --configuration Release --project TrieBasedRouting.csproj
```

## Expected Results

### Performance Targets
- **Standard Routing**: Baseline measurement (typically 2-5ms)
- **Optimized Routing**: < 1ms average response time
- **Trie-Based Routing**: < 0.5ms average response time

### Sample Output
```
=== Ultra-High-Performance Benchmark Results ===
Total Routes: 5000
Average Time: 0.2345 ms
Min Time: 0.1234 ms
Max Time: 0.4567 ms
95th Percentile: 0.3456 ms
99th Percentile: 0.4123 ms
Cache Hit Rate: 85.67%

✅ TARGET ACHIEVED: Average response time is under 1ms!
```

## Performance Analysis

### Time Complexity
- **Standard Routing**: O(n) - Linear search through routes
- **Optimized Routing**: O(n) with caching - Linear search with cache hits
- **Trie-Based Routing**: O(log n) - Logarithmic search through trie

### Memory Usage
- **Standard Routing**: Low memory overhead
- **Optimized Routing**: Moderate memory for caching
- **Trie-Based Routing**: Higher memory for trie structure, but shared prefixes

### Scalability
- **Standard Routing**: Performance degrades linearly with route count
- **Optimized Routing**: Performance improves with cache hit rate
- **Trie-Based Routing**: Performance remains consistent regardless of route count

## Optimization Techniques

### 1. Route Caching
- Cache frequently accessed routes
- Use `ConcurrentDictionary` for thread safety
- Implement cache eviction policies

### 2. Compiled Regex
- Pre-compile route patterns
- Use `RegexOptions.Compiled` for better performance
- Avoid runtime regex compilation

### 3. Route Ordering
- Order routes by frequency of use
- Place most common routes first
- Use priority-based sorting

### 4. Trie Data Structure
- Organize routes in prefix tree
- Share common prefixes
- Enable early termination

### 5. Memory Optimization
- Reduce string allocations
- Use value types where possible
- Implement object pooling

## Advanced Optimizations

### Future Enhancements
1. **SIMD Instructions**: Use vectorized operations for pattern matching
2. **IL Compilation**: Compile routes to IL code for maximum performance
3. **GPU Acceleration**: Use GPU for massive route collections
4. **Memory Mapping**: Use memory-mapped files for large route sets
5. **Route Sharding**: Distribute routes across multiple processors

### Production Considerations
- Memory usage vs. performance trade-offs
- Thread safety for concurrent access
- Cache invalidation strategies
- Monitoring and metrics collection

## Troubleshooting

### Common Issues
1. **High Memory Usage**: Reduce cache size or implement eviction
2. **Slow Performance**: Check route ordering and caching
3. **Thread Safety**: Ensure proper locking mechanisms
4. **Cache Misses**: Analyze route access patterns

### Performance Tuning
1. Monitor cache hit rates
2. Analyze route access patterns
3. Optimize route ordering
4. Consider route consolidation

## Contributing

To contribute to this benchmark:

1. Fork the repository
2. Create a feature branch
3. Implement your optimizations
4. Add performance tests
5. Submit a pull request

## License

This benchmark is provided as-is for educational and performance testing purposes.