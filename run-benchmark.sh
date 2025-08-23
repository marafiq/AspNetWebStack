#!/bin/bash

echo "=== ASP.NET Routing Performance Benchmark Runner ==="
echo "Target: 5000 routes with < 1ms response time"
echo

# Check if .NET Framework is available
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Please install .NET Framework 4.7.2 or later."
    exit 1
fi

echo "✅ .NET SDK found: $(dotnet --version)"

# Restore packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# Build the project
echo "🔨 Building benchmark project..."
dotnet build --configuration Release

# Run the benchmarks
echo ""
echo "🚀 Running routing benchmarks..."
echo ""

# Run the standard benchmark
echo "=== Standard Routing Benchmark ==="
dotnet run --configuration Release --project RoutingBenchmark.csproj

echo ""
echo "=== Optimized Routing Benchmark ==="
# Run the optimized benchmark
dotnet run --configuration Release --project OptimizedRoutingBenchmark.csproj

echo ""
echo "=== Trie-Based Routing Benchmark ==="
# Run the trie-based benchmark
dotnet run --configuration Release --project TrieBasedRouting.csproj

echo ""
echo "=== Benchmark Summary ==="
echo "All benchmarks completed. Check the results above for performance analysis."
echo ""
echo "Performance targets:"
echo "- Standard routing: Baseline measurement"
echo "- Optimized routing: < 1ms average response time"
echo "- Trie-based routing: < 0.5ms average response time"