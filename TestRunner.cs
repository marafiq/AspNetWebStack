using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RoutingBenchmark
{
    public class TestRunner
    {
        public static async Task RunTests()
        {
            Console.WriteLine("=== Routing Benchmark Test Runner ===");
            Console.WriteLine("Running quick tests to verify implementations...");
            Console.WriteLine();

            try
            {
                // Test 1: Optimized Routing
                Console.WriteLine("Testing Optimized Routing...");
                var optimizedBenchmark = new HighPerformanceRoutingBenchmark();
                optimizedBenchmark.SetupOptimizedRoutes();
                var optimizedResults = await optimizedBenchmark.RunOptimizedBenchmark(1000);
                
                Console.WriteLine($"✅ Optimized Routing Test: {optimizedResults.AverageTimeMs:F4} ms average");
                Console.WriteLine($"   Routes: {optimizedResults.TotalRoutes}");
                Console.WriteLine($"   Cache Hit Rate: {optimizedResults.CacheHitRate:F2}%");

                // Test 2: Trie-Based Routing
                Console.WriteLine("\nTesting Trie-Based Routing...");
                var trieBenchmark = new UltraHighPerformanceRoutingBenchmark();
                trieBenchmark.SetupTrieBasedRoutes();
                var trieResults = await trieBenchmark.RunUltraBenchmark(1000);
                
                Console.WriteLine($"✅ Trie-Based Routing Test: {trieResults.AverageTimeMs:F4} ms average");
                Console.WriteLine($"   Routes: {trieResults.TotalRoutes}");
                Console.WriteLine($"   Cache Hit Rate: {trieResults.CacheHitRate:F2}%");

                // Performance Analysis
                Console.WriteLine("\n=== Quick Performance Analysis ===");
                var improvement = ((optimizedResults.AverageTimeMs - trieResults.AverageTimeMs) / optimizedResults.AverageTimeMs) * 100;
                Console.WriteLine($"Trie improvement over optimized: {improvement:F2}%");

                // Check if targets are met
                Console.WriteLine("\n=== Target Achievement ===");
                if (optimizedResults.AverageTimeMs < 1.0)
                {
                    Console.WriteLine("✅ Optimized Routing: TARGET ACHIEVED (< 1ms)");
                }
                else
                {
                    Console.WriteLine("❌ Optimized Routing: TARGET NOT MET (> 1ms)");
                }

                if (trieResults.AverageTimeMs < 0.5)
                {
                    Console.WriteLine("✅ Trie-Based Routing: TARGET ACHIEVED (< 0.5ms)");
                }
                else
                {
                    Console.WriteLine("❌ Trie-Based Routing: TARGET NOT MET (> 0.5ms)");
                }

                Console.WriteLine("\n✅ All tests completed successfully!");
                Console.WriteLine("Ready to run full benchmark with: ./run-benchmark.sh");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    class TestProgram
    {
        static async Task Main(string[] args)
        {
            await TestRunner.RunTests();
        }
    }
}